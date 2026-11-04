using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsWhisker.Controls;
using WindowsWhisker.Models;
using WindowsWhisker.Native;
using WindowsWhisker.Services;

namespace WindowsWhisker.Views;

public partial class PanelWindow : Window
{
    readonly AppBarController _appbar = new();
    readonly DispatcherTimer _hideTimer;
    readonly PanelActions _actions;
    IntPtr _hwnd;
    RECT _target;
    bool _hasTarget;
    bool _applying;
    bool _allowClose;
    bool _unlocked;
    bool _expanded = true;
    bool _fullscreen;
    bool _internalChange;
    bool _resizing;
    int _dragFrom = -1;
    string _buttonSig = "";
    MenuWindow? _menu;
    bool _ownsHotkey;

    public string MonitorDevice { get; private set; } = "";
    public bool OwnsHotkey => _ownsHotkey;

    public PanelWindow()
    {
        InitializeComponent();
        Opacity = 0;
        Width = 1200;
        Height = SettingsStore.Instance.Config.Panel.ThicknessDip;
        _unlocked = !SettingsStore.Instance.Config.Panel.Locked;
        MonitorDevice = SettingsStore.Instance.Config.Panel.MonitorDevice ?? "";
        Left = -32000;
        Top = -32000;
        AllowDrop = true;
        _actions = new PanelActions
        {
            Remove = RemoveItem,
            Move = MoveItem,
            ToggleMenu = ToggleMenu,
            OpenSettings = () => OpenSettings("panel"),
            ChangeLauncher = ChangeLauncher,
            PinPath = PinPath,
            IsLocked = () => SettingsStore.Instance.Config.Panel.Locked,
            ToggleLock = ToggleLock,
            Quit = Quit,
            Owner = () => this
        };
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!IsMouseOver && _menu == null && SettingsStore.Instance.Config.Panel.AutoHideMode == "Always")
            {
                _expanded = false;
                ApplyGeometry();
            }
        };
        WindowTracker.Instance.Changed += OnWindows;
        SettingsStore.Instance.AreaChanged += OnConfig;
        DragOver += OnDragOver;
        Drop += OnDrop;
        Rebuild();
        ApplyChrome();
    }

    public PanelWindow(string monitorDevice, bool ownsHotkey) : this()
    {
        MonitorDevice = monitorDevice ?? "";
        _ownsHotkey = ownsHotkey;
    }

    public void AllowClose() => _allowClose = true;

    public void SetOwnsHotkey(bool on)
    {
        _ownsHotkey = on;
        UpdateHotkey();
    }

    public bool ContainsScreenPoint(POINT pt) =>
        _hasTarget &&
        pt.X >= _target.Left && pt.X < _target.Right &&
        pt.Y >= _target.Top && pt.Y < _target.Bottom;

    public void ToggleMenuFromHotkey() => ToggleMenu(MenuAnchor());

    void Quit() => PanelManager.Instance.QuitAll();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        long ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        ex &= ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
        _appbar.Attach(_hwnd);
        ApplyGeometry();
        UpdateHotkey();
        ApplyOpacity(false);
        if (SettingsStore.Instance.Config.Panel.AutoHideMode == "Always")
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); QueueHide(); };
            timer.Start();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }
        WindowTracker.Instance.Changed -= OnWindows;
        if (_hwnd != IntPtr.Zero) Win32.UnregisterHotKey(_hwnd, 1);
        _appbar.Unregister();
        _menu?.Close();
        base.OnClosing(e);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            return WndProcCore(hwnd, msg, wParam, lParam, ref handled);
        }
        catch (Exception ex)
        {
            Log.Error("wndproc", ex);
            return IntPtr.Zero;
        }
    }

    IntPtr WndProcCore(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_QUERYENDSESSION)
            _allowClose = true;

        if (msg == Win32.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)Win32.MA_NOACTIVATE;
        }

        if (msg == Win32.WM_HOTKEY)
        {
            PanelManager.Instance.ToggleMenuAtCursor();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == Win32.WM_WINDOWPOSCHANGING && _hasTarget && !_fullscreen)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            pos.x = _target.Left;
            pos.y = _target.Top;
            pos.cx = Math.Max(1, _target.Width);
            pos.cy = Math.Max(1, _target.Height);
            pos.flags &= ~(Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
            Marshal.StructureToPtr(pos, lParam, false);
        }

        int code = _appbar.Consume(msg, wParam, lParam);
        if (code == Win32.ABN_POSCHANGED && !_fullscreen && !_applying)
            Dispatcher.BeginInvoke(ApplyGeometry);
        else if (code == Win32.ABN_FULLSCREENAPP)
        {
            bool on = lParam != IntPtr.Zero;
            Dispatcher.BeginInvoke(() =>
            {
                if (on && IsTransientOverlay()) return;
                SetFullscreen(on);
            });
        }

        if (msg is Win32.WM_DISPLAYCHANGE or Win32.WM_DPICHANGED or Win32.WM_SETTINGCHANGE)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyGeometry();
                if (msg == Win32.WM_DISPLAYCHANGE)
                    PanelManager.Instance.NotifyDisplays();
            });
        }

        return IntPtr.Zero;
    }

    static bool IsTransientOverlay()
    {
        try
        {
            var hwnd = Win32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return true;
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            string name = Path.GetFileNameWithoutExtension(Win32.QueryProcessPath(pid) ?? "");
            return name.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase)
                || name.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ScreenSketch", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PickerHost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    void OnConfig(ConfigArea area)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnConfig(area));
            return;
        }
        switch (area)
        {
            case ConfigArea.PanelGeometry:
                SetUnlocked(!SettingsStore.Instance.Config.Panel.Locked, persist: false);
                ApplyGeometry();
                if (IsVertical() != Strip.IsVertical)
                    Rebuild();
                ApplyChrome();
                ApplyOpacity(IsMouseOver);
                break;
            case ConfigArea.PanelItems:
                if (_internalChange) return;
                Rebuild();
                break;
            case ConfigArea.PanelChrome:
                ApplyChrome();
                Rebuild();
                break;
            case ConfigArea.Menu:
                UpdateHotkey();
                if (ButtonSig() != _buttonSig)
                    Rebuild();
                _menu?.Reload();
                break;
        }
    }

    void ApplyGeometry()
    {
        if (_applying || _hwnd == IntPtr.Zero || _fullscreen) return;
        _applying = true;
        try
        {
            if (!_appbar.TryApply(SettingsStore.Instance.Config.Panel, _expanded, out var rc, MonitorDevice))
                return;
            _target = rc;
            _hasTarget = true;
            uint dpi = Win32.GetDpiForWindow(_hwnd);
            if (dpi == 0) dpi = 96;
            double scale = dpi / 96.0;
            Left = rc.Left / scale;
            Top = rc.Top / scale;
            Width = Math.Max(1, rc.Width / scale);
            Height = Math.Max(1, rc.Height / scale);
            Frame.BorderThickness = PanelBorder();
        }
        catch (Exception ex)
        {
            Log.Error("appbar", ex);
        }
        finally
        {
            _applying = false;
        }
    }

    Thickness PanelBorder()
    {
        int width = _unlocked ? Math.Max(1, SettingsStore.Instance.Config.Panel.BorderWidth) : SettingsStore.Instance.Config.Panel.BorderWidth;
        return new Thickness(Math.Clamp(width, 0, 20));
    }

    void ApplyChrome()
    {
        var panel = SettingsStore.Instance.Config.Panel;
        Background = panel.BackgroundStyle == "None" ? Brushes.Transparent : Ui.PanelBackground();
        Frame.BorderBrush = _unlocked ? Ui.Accent() : new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
        Frame.BorderThickness = PanelBorder();
        ApplyOpacity(IsMouseOver);
    }

    void ApplyOpacity(bool over)
    {
        var panel = SettingsStore.Instance.Config.Panel;
        double value = (over ? panel.OpacityEnter : panel.OpacityLeave) / 100.0;
        Opacity = Math.Clamp(value, 0.05, 1);
    }

    void OnWindows()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnWindows);
            return;
        }
        if (SettingsStore.Instance.Config.Panel.AutoHideMode != "Intelligent") return;
        bool cover = PanelCovered();
        if (cover && !IsMouseOver && _menu == null && _expanded)
        {
            _expanded = false;
            ApplyGeometry();
        }
        else if (!cover && !_expanded)
        {
            _expanded = true;
            ApplyGeometry();
        }
    }

    bool PanelCovered()
    {
        foreach (var window in WindowTracker.Instance.Windows)
        {
            if (window.IsMinimized) continue;
            if (!VirtualDesktopService.Instance.IsOnCurrentDesktop(window.Handle)) continue;
            if (Win32.IsZoomed(window.Handle)) return true;
            if (_hasTarget &&
                window.Left < _target.Right && window.Right > _target.Left &&
                window.Top < _target.Bottom && window.Bottom > _target.Top)
                return true;
        }
        return false;
    }

    void Rebuild()
    {
        Strip.Children.Clear();
        bool vertical = IsVertical();
        Strip.IsVertical = vertical;
        foreach (var item in SettingsStore.Instance.Config.Panel.Items)
        {
            var slot = new Grid { Tag = item, Background = Brushes.Transparent };
            slot.Children.Add(ElementViews.Create(item, _actions, vertical));
            var overlay = BuildOverlay(item);
            slot.Children.Add(overlay);
            if (item.Kind is PanelKinds.TaskList or PanelKinds.Spacer ||
                (item.Kind == PanelKinds.Separator && item.Expand))
                StripPanel.SetFlex(slot, true);
            HookDrag(slot, overlay);
            Strip.Children.Add(slot);
        }
        _buttonSig = ButtonSig();
        ApplyUnlockVisuals();
    }

    string ButtonSig()
    {
        var menu = SettingsStore.Instance.Config.Menu;
        var panel = SettingsStore.Instance.Config.Panel;
        return menu.ButtonDisplay + "|" + menu.ButtonTitle + "|" + menu.ButtonIconPath + "|" + menu.SinglePanelRow + "|" + panel.Height + "|" + panel.Edge;
    }

    Border BuildOverlay(PanelItem item)
    {
        var overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 59, 120, 224)),
            BorderBrush = Ui.Accent(),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.SizeAll,
            ClipToBounds = true,
            ToolTip = item.Summary,
            Tag = "overlay"
        };
        var grid = new Grid();
        var remove = new Button
        {
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            FontSize = 8,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Focusable = false,
            SnapsToDevicePixels = true,
            Content = new TextBlock
            {
                Text = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 8,
                LineHeight = 8,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                IsHitTestVisible = false
            }
        };
        remove.Click += (_, _) => RemoveItem(item);
        grid.Children.Add(remove);
        overlay.Child = grid;
        overlay.MouseRightButtonUp += (_, e) =>
        {
            ShowPanelMenu();
            e.Handled = true;
        };
        return overlay;
    }

    void HookDrag(Grid slot, Border overlay)
    {
        bool moved = false;
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            if (!_unlocked || e.OriginalSource is Button) return;
            moved = false;
            _dragFrom = Strip.Children.IndexOf(slot);
            overlay.CaptureMouse();
            e.Handled = true;
        };
        overlay.MouseMove += (_, e) =>
        {
            if (_dragFrom < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            int over = HitIndex(e.GetPosition(Strip));
            if (over >= 0 && over != _dragFrom)
            {
                MoveVisual(_dragFrom, over);
                _dragFrom = over;
                moved = true;
            }
        };
        overlay.MouseLeftButtonUp += (_, _) =>
        {
            _dragFrom = -1;
            if (overlay.IsMouseCaptured) overlay.ReleaseMouseCapture();
            if (!moved) return;
            moved = false;
            Dispatcher.BeginInvoke(() => SettingsStore.Instance.NotifyItems(), DispatcherPriority.Background);
        };
    }

    int HitIndex(Point pos)
    {
        bool vertical = Strip.IsVertical;
        double cursor = vertical ? pos.Y : pos.X;
        double start = 0;
        for (int i = 0; i < Strip.Children.Count; i++)
        {
            var child = (FrameworkElement)Strip.Children[i];
            double length = vertical ? child.ActualHeight : child.ActualWidth;
            if (cursor < start + length) return i;
            start += length;
        }
        return Strip.Children.Count - 1;
    }

    void MoveVisual(int from, int to)
    {
        var items = SettingsStore.Instance.Config.Panel.Items;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
        var child = Strip.Children[from];
        Strip.Children.RemoveAt(from);
        Strip.Children.Insert(to, child);
    }

    void MoveItem(PanelItem item, int delta)
    {
        int index = SettingsStore.Instance.Config.Panel.Items.FindIndex(i => i.Id == item.Id);
        int next = index + delta;
        var items = SettingsStore.Instance.Config.Panel.Items;
        if (index < 0 || next < 0 || next >= items.Count) return;
        _internalChange = true;
        try
        {
            MoveVisual(index, next);
            SettingsStore.Instance.NotifyItems();
        }
        finally
        {
            _internalChange = false;
        }
    }

    void RemoveItem(PanelItem item)
    {
        SettingsStore.Instance.Update(c => c.Panel.Items.RemoveAll(i => i.Id == item.Id), ConfigArea.PanelItems);
    }

    void AddKind(string kind)
    {
        var item = new PanelItem { Kind = kind };
        SettingsStore.Instance.Update(c => InsertSmart(c.Panel.Items, item), ConfigArea.PanelItems);
    }

    void AddLauncher()
    {
        var app = Dialogs.Pick(this);
        if (app == null) return;
        PinEntry(app);
    }

    void ChangeLauncher(PanelItem item)
    {
        var app = Dialogs.Pick(this);
        if (app == null) return;
        item.Label = app.Name;
        item.Target = TargetOf(app);
        item.Arguments = app.Arguments;
        item.WorkingDirectory = app.WorkingDirectory;
        item.IconPath = app.IconPath;
        item.IconIndex = app.IconIndex;
        SettingsStore.Instance.Update(_ => { }, ConfigArea.PanelItems);
    }

    void PinPath(string path, string label)
    {
        var item = PanelItem.Launcher(path, string.IsNullOrWhiteSpace(label) ? Path.GetFileNameWithoutExtension(path) : label);
        item.IconPath = path;
        SettingsStore.Instance.Update(c => InsertSmart(c.Panel.Items, item), ConfigArea.PanelItems);
    }

    public void PinApp(AppEntry app) => PinEntry(app);

    void PinEntry(AppEntry app)
    {
        var item = new PanelItem
        {
            Kind = PanelKinds.Launcher,
            Label = app.Name,
            Target = TargetOf(app),
            Arguments = app.Arguments,
            WorkingDirectory = app.WorkingDirectory,
            IconPath = string.IsNullOrWhiteSpace(app.IconPath) ? app.TargetPath : app.IconPath,
            IconIndex = app.IconIndex
        };
        SettingsStore.Instance.Update(c => InsertSmart(c.Panel.Items, item), ConfigArea.PanelItems);
    }

    static string TargetOf(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.TargetPath)) return app.TargetPath;
        if (!string.IsNullOrWhiteSpace(app.AppUserModelId)) return "aumid:" + app.AppUserModelId;
        return app.Key;
    }

    static void InsertSmart(List<PanelItem> items, PanelItem item)
    {
        if (item.Kind == PanelKinds.Settings)
        {
            int clock = items.FindLastIndex(i => i.Kind == PanelKinds.Clock);
            if (clock >= 0) items.Insert(clock + 1, item);
            else items.Add(item);
            return;
        }
        if (item.Kind == PanelKinds.CpuGraph)
        {
            int clock = items.FindLastIndex(i => i.Kind == PanelKinds.Clock);
            if (clock >= 0) items.Insert(clock, item);
            else items.Add(item);
            return;
        }
        int index = items.FindIndex(i =>
            i.Kind is PanelKinds.TaskList or PanelKinds.Spacer or PanelKinds.DesktopShifter or PanelKinds.Clock ||
            (i.Kind == PanelKinds.Separator && i.Expand));
        if (index < 0) items.Add(item);
        else items.Insert(index, item);
    }

    void ToggleMenu(FrameworkElement anchor)
    {
        if (_menu != null)
        {
            _menu.Close();
            _menu = null;
            return;
        }
        _expanded = true;
        ApplyGeometry();
        _menu = new MenuWindow(() => OpenSettings("appearance"), Quit);
        _menu.Closed += (_, _) =>
        {
            _menu = null;
            QueueHide();
        };
        _menu.Place(anchor);
        _menu.Show();
        _menu.Activate();
    }

    FrameworkElement MenuAnchor()
    {
        foreach (var child in Strip.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is PanelItem { Kind: PanelKinds.Menu })
                return fe;
        }
        return this;
    }

    public void OpenSettings(string tab) => PanelManager.Instance.OpenSettings(tab);

    void UpdateHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.UnregisterHotKey(_hwnd, 1);
        if (_ownsHotkey && SettingsStore.Instance.Config.Menu.AltF1)
            Win32.RegisterHotKey(_hwnd, 1, Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x70);
    }

    void SetFullscreen(bool on)
    {
        _fullscreen = on;
        if (on)
        {
            _appbar.Unregister();
            _hasTarget = false;
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        }
        else
        {
            Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
            ApplyGeometry();
        }
    }

    void ToggleLock() => SetUnlocked(SettingsStore.Instance.Config.Panel.Locked);

    void SetUnlocked(bool on, bool persist = true)
    {
        _unlocked = on;
        ApplyUnlockVisuals();
        ApplyChrome();
        if (!persist || SettingsStore.Instance.Config.Panel.Locked == !on) return;
        SettingsStore.Instance.Update(c => c.Panel.Locked = !on, ConfigArea.PanelGeometry);
    }

    void ApplyUnlockVisuals()
    {
        foreach (var child in Strip.Children)
        {
            if (child is not Grid grid) continue;
            foreach (var element in grid.Children)
            {
                if (element is Border { Tag: "overlay" } overlay)
                    overlay.Visibility = _unlocked ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    bool IsVertical() => SettingsStore.Instance.Config.Panel.Edge is "Left" or "Right";

    void QueueHide()
    {
        if (SettingsStore.Instance.Config.Panel.AutoHideMode == "Intelligent")
        {
            OnWindows();
            return;
        }
        if (SettingsStore.Instance.Config.Panel.AutoHideMode != "Always") return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _hideTimer.Stop();
        ApplyOpacity(over: true);
        if (SettingsStore.Instance.Config.Panel.AutoHide && !_expanded)
        {
            _expanded = true;
            ApplyGeometry();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ApplyOpacity(over: false);
        QueueHide();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_resizing)
        {
            ResizeToCursor();
            return;
        }
        Cursor = _unlocked && OnGrip(e.GetPosition(this)) ? GripCursor() : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_unlocked && OnGrip(e.GetPosition(this)))
        {
            _resizing = true;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_resizing) return;
        _resizing = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        SettingsStore.Instance.SaveQuiet();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (e.Handled) return;
        ShowPanelMenu();
        e.Handled = true;
    }

    bool OnGrip(Point local)
    {
        const double band = 5;
        return SettingsStore.Instance.Config.Panel.Edge switch
        {
            "Bottom" => local.Y <= band,
            "Left" => local.X >= ActualWidth - band,
            "Right" => local.X <= band,
            _ => local.Y >= ActualHeight - band
        };
    }

    Cursor GripCursor() => SettingsStore.Instance.Config.Panel.Edge is "Left" or "Right" ? Cursors.SizeWE : Cursors.SizeNS;

    void ResizeToCursor()
    {
        if (!Win32.GetCursorPos(out var cursor) || _hwnd == IntPtr.Zero) return;
        var monitors = Win32.GetMonitors();
        var mon = monitors.FirstOrDefault(m =>
            cursor.X >= m.Bounds.Left && cursor.X < m.Bounds.Right &&
            cursor.Y >= m.Bounds.Top && cursor.Y < m.Bounds.Bottom);
        if (mon.Handle == IntPtr.Zero) return;
        uint dpi = Win32.GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        int px = SettingsStore.Instance.Config.Panel.Edge switch
        {
            "Bottom" => mon.Bounds.Bottom - cursor.Y,
            "Left" => cursor.X - mon.Bounds.Left,
            "Right" => mon.Bounds.Right - cursor.X,
            _ => cursor.Y - mon.Bounds.Top
        };
        int dip = (int)Math.Round(px * 96.0 / dpi);
        int rows = Math.Max(1, SettingsStore.Instance.Config.Panel.Rows);
        int row = Math.Clamp(dip / rows, 16, 128);
        if (row == SettingsStore.Instance.Config.Panel.Height) return;
        SettingsStore.Instance.Update(c => c.Panel.Height = row, ConfigArea.PanelGeometry);
    }

    void ShowPanelMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Panel settings", () => OpenSettings("panel")));
        menu.Items.Add(Item("Menu settings", () => OpenSettings("appearance")));
        menu.Items.Add(Item(SettingsStore.Instance.Config.Panel.Locked ? "Unlock panel" : "Lock panel", ToggleLock));
        menu.Items.Add(new Separator());
        var add = new MenuItem { Header = "Add element" };
        add.Items.Add(Item("Applications menu", () => AddKind(PanelKinds.Menu)));
        add.Items.Add(Item("Launcher shortcut…", AddLauncher));
        add.Items.Add(Item("Window buttons", () => AddKind(PanelKinds.TaskList)));
        add.Items.Add(Item("Desktop shifter", () => AddKind(PanelKinds.DesktopShifter)));
        add.Items.Add(Item("Clock", () => AddKind(PanelKinds.Clock)));
        add.Items.Add(Item("Separator", () => AddKind(PanelKinds.Separator)));
        add.Items.Add(Item("Spacer", () => AddKind(PanelKinds.Spacer)));
        add.Items.Add(Item("Clipboard", () => AddKind(PanelKinds.Clipboard)));
        add.Items.Add(Item("Show desktop", () => AddKind(PanelKinds.ShowDesktop)));
        add.Items.Add(Item("Settings", () => AddKind(PanelKinds.Settings)));
        add.Items.Add(Item("CPU graph", () => AddKind(PanelKinds.CpuGraph)));
        menu.Items.Add(add);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open crash log", Log.OpenLogs));
        menu.Items.Add(Item("Quit", Quit));
        ContextMenus.Show(menu, this);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(Ui.AppDragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(Ui.AppDragFormat) is not string key) return;
        var app = AppCatalogService.Instance.Find(key);
        if (app != null) PinEntry(app);
    }

    static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
