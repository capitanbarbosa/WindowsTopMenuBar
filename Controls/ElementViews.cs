using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsWhisker.Models;
using WindowsWhisker.Native;
using WindowsWhisker.Services;
using WindowsWhisker.Views;

namespace WindowsWhisker.Controls;

public static class ElementViews
{
    public static FrameworkElement Create(PanelItem item, PanelActions actions, bool vertical)
    {
        FrameworkElement view = item.Kind switch
        {
            PanelKinds.Menu => MenuButton(item, actions, vertical),
            PanelKinds.Launcher => LauncherButton(item, actions, vertical),
            PanelKinds.TaskList => new TaskListView(actions, vertical),
            PanelKinds.DesktopShifter => new DesktopShifterView(actions, vertical),
            PanelKinds.Clock => new ClockView(actions, vertical),
            PanelKinds.ShowDesktop => ShowDesktopButton(item, actions, vertical),
            PanelKinds.Clipboard => ClipboardButton(item, actions, vertical),
            PanelKinds.Settings => SettingsButton(item, actions, vertical),
            PanelKinds.Spacer => Spacer(item, actions, vertical),
            _ => Separator(item, actions, vertical)
        };
        view.Tag = item;
        return view;
    }

    public static void ShowItemMenu(PanelItem item, PanelActions actions, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        if (item.Kind == PanelKinds.Separator)
            menu.Items.Add(Entry("Properties", () => Dialogs.SeparatorProperties(actions.Owner(), item)));
        if (item.Kind == PanelKinds.Launcher)
            menu.Items.Add(Entry("Change application…", () => actions.ChangeLauncher(item)));
        menu.Items.Add(Entry("Move left", () => actions.Move(item, -1)));
        menu.Items.Add(Entry("Move right", () => actions.Move(item, 1)));
        menu.Items.Add(Entry("Remove", () => actions.Remove(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry("Panel settings", actions.OpenSettings));
        menu.Items.Add(Entry(actions.IsLocked() ? "Unlock panel" : "Lock panel", actions.ToggleLock));
        menu.Items.Add(Entry("Quit", actions.Quit));
        ContextMenus.Show(menu, anchor);
    }

    static FrameworkElement MenuButton(PanelItem item, PanelActions actions, bool vertical)
    {
        var config = SettingsStore.Instance.Config.Menu;
        double icon = config.SinglePanelRow ? 18 : Ui.PanelIcon();
        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        if (config.ButtonDisplay is "Icon" or "IconAndTitle")
            panel.Children.Add(Ui.IconOrMark(config.ButtonIconPath, icon));
        if (config.ButtonDisplay is "Title" or "IconAndTitle")
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(config.ButtonTitle) ? "Applications" : config.ButtonTitle,
                Foreground = Ui.PanelText(),
                Margin = new Thickness(config.ButtonDisplay == "IconAndTitle" ? 8 : 0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            });
        }

        var button = Ui.Flat();
        button.Content = panel;
        button.Padding = new Thickness(10, 0, 10, 0);
        button.ToolTip = string.IsNullOrWhiteSpace(config.ButtonTitle) ? "Applications" : config.ButtonTitle;
        button.Click += (_, _) => actions.ToggleMenu(button);
        AttachItemMenu(button, item, actions);
        return button;
    }

    static FrameworkElement LauncherButton(PanelItem item, PanelActions actions, bool vertical)
    {
        var button = Ui.Flat();
        double iconSize = Ui.LauncherIcon();
        var icon = IconService.Get(string.IsNullOrWhiteSpace(item.IconPath) ? item.Target : item.IconPath, item.IconIndex, (int)Math.Ceiling(iconSize));
        FrameworkElement content = icon != null
            ? new Image { Source = icon, Width = iconSize, Height = iconSize, Stretch = Stretch.Uniform }
            : new TextBlock
            {
                Text = (string.IsNullOrWhiteSpace(item.Label) ? "A" : item.Label)[0].ToString(),
                Foreground = Ui.PanelText(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        if (content is Image image)
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        button.Content = content;
        button.Padding = new Thickness(6, 0, 6, 0);
        button.ToolTip = string.IsNullOrWhiteSpace(item.Label) ? item.Target : item.Label;
        button.Click += (_, _) =>
        {
            try
            {
                AppCatalogService.LaunchTarget(item.Target, item.Arguments, item.WorkingDirectory);
            }
            catch (Exception ex)
            {
                Dialogs.Alert(actions.Owner(), "Couldn't open that shortcut.\n" + ex.Message);
            }
        };
        AttachItemMenu(button, item, actions);
        return button;
    }

    static FrameworkElement ShowDesktopButton(PanelItem item, PanelActions actions, bool vertical)
    {
        var button = Ui.Flat();
        button.Width = vertical ? double.NaN : 14;
        button.Height = vertical ? 14 : double.NaN;
        button.Margin = new Thickness(2);
        button.ToolTip = "Show desktop";
        button.Content = new Border
        {
            Width = vertical ? 16 : 2,
            Height = vertical ? 2 : 16,
            Background = Ui.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => SessionCommands.ShowDesktop();
        AttachItemMenu(button, item, actions);
        return button;
    }

    static FrameworkElement ClipboardButton(PanelItem item, PanelActions actions, bool vertical)
    {
        var button = Ui.Flat();
        button.ToolTip = "Clipboard";
        button.Padding = new Thickness(8, 0, 8, 0);
        var row = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Max(14, Ui.PanelIcon() - 2),
            Foreground = Ui.PanelText(),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!vertical)
        {
            row.Children.Add(new TextBlock
            {
                Text = "Clipboard",
                Foreground = Ui.PanelText(),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });
        }
        button.Content = row;
        button.Click += (_, _) => SessionCommands.OpenClipboard();
        AttachItemMenu(button, item, actions);
        return button;
    }

    static FrameworkElement SettingsButton(PanelItem item, PanelActions actions, bool vertical)
    {
        var button = Ui.Flat();
        button.ToolTip = "Settings";
        button.Padding = new Thickness(6, 0, 10, 0);
        button.Content = new TextBlock
        {
            Text = Ui.Glyph("settings"),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Max(14, Ui.PanelIcon() - 2),
            Foreground = Ui.PanelText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => actions.OpenSettings();
        AttachItemMenu(button, item, actions);
        return button;
    }

    static FrameworkElement Spacer(PanelItem item, PanelActions actions, bool vertical)
    {
        var space = new Border
        {
            MinWidth = vertical ? 0 : 16,
            MinHeight = vertical ? 16 : 0,
            Background = Brushes.Transparent,
            ToolTip = "Spacer"
        };
        AttachItemMenu(space, item, actions);
        return space;
    }

    static FrameworkElement Separator(PanelItem item, PanelActions actions, bool vertical)
    {
        bool expand = item.Expand;
        double compact = item.Style is "Handle" or "Dots" ? 16 : 12;
        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinWidth = vertical ? 0 : 8,
            MinHeight = vertical ? 8 : 0,
            Width = vertical || expand ? double.NaN : compact,
            Height = !vertical || expand ? double.NaN : compact,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Separator"
        };
        var mark = SeparatorMark(item.Style, vertical);
        mark.IsHitTestVisible = false;
        host.Children.Add(mark);
        AttachItemMenu(host, item, actions);
        host.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                Dialogs.SeparatorProperties(actions.Owner(), item);
                e.Handled = true;
            }
        };
        return host;
    }

    static FrameworkElement SeparatorMark(string? style, bool vertical)
    {
        return style switch
        {
            "Transparent" => new Border { Background = Brushes.Transparent },
            "Handle" => HandleMark(vertical),
            "Dots" => DotsMark(vertical),
            _ => new Border
            {
                Width = vertical ? 18 : 1,
                Height = vertical ? 1 : 18,
                Background = Ui.Line,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    static FrameworkElement HandleMark(bool vertical)
    {
        var row = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (int i = 0; i < 3; i++)
        {
            row.Children.Add(new Border
            {
                Width = vertical ? 12 : 1,
                Height = vertical ? 1 : 12,
                Margin = vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0),
                Background = Ui.Muted
            });
        }
        return row;
    }

    static FrameworkElement DotsMark(bool vertical)
    {
        var row = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (int i = 0; i < 3; i++)
        {
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 3,
                Height = 3,
                Margin = vertical ? new Thickness(0, 2, 0, 2) : new Thickness(2, 0, 2, 0),
                Fill = Ui.Muted
            });
        }
        return row;
    }

    static void AttachItemMenu(FrameworkElement element, PanelItem item, PanelActions actions)
    {
        element.MouseRightButtonUp += (_, e) =>
        {
            ShowItemMenu(item, actions, element);
            e.Handled = true;
        };
    }

    static MenuItem Entry(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}

public sealed class ClockView : Button
{
    readonly TextBlock _text;
    readonly DispatcherTimer _timer;
    readonly bool _vertical;

    public ClockView(PanelActions actions, bool vertical)
    {
        _vertical = vertical;
        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        if (Application.Current?.TryFindResource("FlatButton") is Style flat)
            Style = flat;
        Padding = new Thickness(10, 0, 10, 0);
        Margin = new Thickness(1, 3, 1, 3);
        Focusable = false;
        _text = new TextBlock
        {
            Foreground = Ui.PanelText(),
            FontSize = vertical ? 10 : 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Content = _text;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RenderTime();
        Loaded += (_, _) => { RenderTime(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Click += (_, _) => OpenFlyout(actions);
        ToolTip = DateTime.Now.ToString("dddd, MMMM d, yyyy");
    }

    void RenderTime()
    {
        var now = DateTime.Now;
        _text.Text = _vertical ? now.ToString("HH:mm") : now.ToString("ddd  d MMM   HH:mm");
        ToolTip = now.ToString("dddd, MMMM d, yyyy  h:mm tt");
    }

    void OpenFlyout(PanelActions actions)
    {
        var window = new Window
        {
            Width = 240,
            Height = 150,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Background = Ui.Menu,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = BuildFlyout()
        };
        Ui.DarkTitle(window);
        window.Deactivated += (_, _) => window.Close();
        var source = PresentationSource.FromVisual(this);
        if (source != null)
        {
            var px = PointToScreen(new Point(0, ActualHeight));
            var dip = source.CompositionTarget.TransformFromDevice.Transform(px);
            window.Left = dip.X;
            window.Top = SettingsStore.Instance.Config.Panel.Edge == "Bottom" ? dip.Y - window.Height - ActualHeight : dip.Y;
        }
        window.Show();
    }

    UIElement BuildFlyout()
    {
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("dddd"),
            Foreground = Ui.Muted,
            FontSize = 12
        });
        root.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("MMMM d"),
            Foreground = Ui.PanelText(),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 12)
        });
        var button = new Button { Content = "Date and time settings", HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("date settings", ex);
            }
            Window.GetWindow(button)?.Close();
        };
        root.Children.Add(button);
        return root;
    }
}

public sealed class TaskListView : Grid
{
    readonly PanelActions _actions;
    readonly bool _vertical;
    readonly ScrollViewer _scroll;
    readonly StackPanel _row;
    string _signature = "";

    public TaskListView(PanelActions actions, bool vertical)
    {
        _actions = actions;
        _vertical = vertical;
        MinWidth = 0;
        MinHeight = 0;
        _row = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
        _scroll = new ScrollViewer
        {
            Content = _row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            CanContentScroll = false
        };
        Children.Add(_scroll);
        WindowTracker.Instance.Changed += OnChanged;
        Unloaded += (_, _) => WindowTracker.Instance.Changed -= OnChanged;
        SizeChanged += (_, _) => Rebuild(force: false);
        OnChanged();
    }

    void OnChanged() => Rebuild(force: false);

    void Rebuild(bool force)
    {
        var windows = WindowTracker.Instance.Windows
            .Where(w => VirtualDesktopService.Instance.IsOnCurrentDesktop(w.Handle))
            .Take(40)
            .ToList();
        string signature = string.Join("|", windows.Select(w => w.Handle.ToInt64() + ":" + w.Title + ":" + (w.IsForeground ? "1" : "0") + ":" + (w.Icon != null ? "i" : "")));
        double avail = _vertical ? ActualHeight : ActualWidth;
        if (!force && signature == _signature && _row.Children.Count == windows.Count)
        {
            Resize(windows.Count, avail);
            return;
        }
        _signature = signature;
        _row.Children.Clear();
        foreach (var window in windows)
            _row.Children.Add(MakeButton(window));
        Resize(windows.Count, avail);
    }

    void Resize(int count, double avail)
    {
        if (count == 0 || avail < 8) return;
        double fit = (avail - 4) / count;
        bool overflow = fit < 46;
        double length = overflow ? 168 : Math.Min(200, Math.Max(46, fit));
        if (_vertical)
            _scroll.VerticalScrollBarVisibility = overflow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        else
            _scroll.HorizontalScrollBarVisibility = overflow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        foreach (Button button in _row.Children)
        {
            if (_vertical) button.Height = length;
            else button.Width = length;
        }
    }

    Button MakeButton(WindowInfo window)
    {
        var button = Ui.Flat();
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(6, 0, 6, 0);
        button.Margin = new Thickness(1, 3, 1, 3);
        if (window.IsForeground)
            button.Background = new SolidColorBrush(Color.FromArgb(150, Ui.Accent().Color.R, Ui.Accent().Color.G, Ui.Accent().Color.B));
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        int textColumn = 0;
        if (window.Icon != null)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Source = window.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, _vertical ? 0 : 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);
            textColumn = 1;
        }
        if (!_vertical)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var title = new TextBlock
            {
                Text = window.Title,
                Foreground = Ui.PanelText(),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(title, textColumn);
            row.Children.Add(title);
        }
        button.Content = row;
        button.ToolTip = window.Title;
        button.Click += (_, _) => WindowTracker.Instance.Activate(window.Handle);
        button.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                WindowTracker.Instance.Close(window.Handle);
                e.Handled = true;
            }
        };
        button.MouseRightButtonUp += (_, e) =>
        {
            ShowWindowMenu(window, button);
            e.Handled = true;
        };
        return button;
    }

    void ShowWindowMenu(WindowInfo window, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(window.IsForeground && !window.IsMinimized ? "Minimize" : "Activate", () => WindowTracker.Instance.Activate(window.Handle)));
        menu.Items.Add(Item("Maximize", () => WindowTracker.Instance.Maximize(window.Handle)));
        menu.Items.Add(Item("Close", () => WindowTracker.Instance.Close(window.Handle)));
        if (!string.IsNullOrWhiteSpace(window.ProcessPath))
            menu.Items.Add(Item("Pin to panel", () => _actions.PinPath(window.ProcessPath, window.ProcessName)));

        var desktops = VirtualDesktopService.Instance.GetDesktops();
        if (desktops.Count > 0)
        {
            var move = new MenuItem { Header = "Move to desktop" };
            foreach (var desktop in desktops)
            {
                var id = desktop.Id;
                move.Items.Add(Item(desktop.Name, () =>
                {
                    try { VirtualDesktopService.Instance.MoveWindowToDesktop(window.Handle, id); }
                    catch (Exception ex) { Dialogs.Alert(_actions.Owner(), ex.Message); }
                }));
            }
            menu.Items.Add(move);
        }
        ContextMenus.Show(menu, anchor);
    }

    static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = Ui.MenuLabel(header) };
        item.Click += (_, _) => action();
        return item;
    }
}

public sealed class DesktopShifterView : StackPanel
{
    readonly PanelActions _actions;
    readonly bool _vertical;
    readonly DispatcherTimer _timer;
    readonly List<Border> _cells = [];
    string _signature = "";
    IReadOnlyList<DesktopInfo> _desktops = [];

    public DesktopShifterView(PanelActions actions, bool vertical)
    {
        _actions = actions;
        _vertical = vertical;
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        Margin = new Thickness(vertical ? 0 : 4, vertical ? 4 : 0, vertical ? 0 : 4, vertical ? 4 : 0);
        MouseWheel += OnWheel;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    void Refresh()
    {
        if (!VirtualDesktopService.Instance.IsAvailable)
        {
            if (Children.Count == 0)
            {
                var broken = Ui.Flat();
                broken.Content = new TextBlock { Text = "!", Foreground = Ui.PanelText(), FontWeight = FontWeights.Bold };
                broken.ToolTip = VirtualDesktopService.Instance.Error ?? "Virtual desktops are unavailable on this Windows build.";
                Children.Add(broken);
            }
            return;
        }

        IReadOnlyList<DesktopInfo> desktops;
        try { desktops = VirtualDesktopService.Instance.GetDesktops(); }
        catch (Exception ex)
        {
            Log.Error("desktops", ex);
            return;
        }
        _desktops = desktops;
        string signature = string.Join("|", desktops.Select(d => d.Id.ToString() + d.Name));
        if (signature != _signature)
        {
            _signature = signature;
            Children.Clear();
            _cells.Clear();
            foreach (var desktop in desktops)
            {
                var cell = MakeCell(desktop);
                _cells.Add(cell);
                Children.Add(cell);
            }
            var add = Ui.Flat();
            add.Width = 18;
            add.Margin = new Thickness(0);
            add.Padding = new Thickness(0);
            add.Content = new TextBlock { Text = "+", Foreground = Ui.PanelText(), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center };
            add.ToolTip = "Add a desktop";
            add.Click += (_, _) =>
            {
                try { VirtualDesktopService.Instance.Create(); }
                catch (Exception ex) { Dialogs.Alert(_actions.Owner(), ex.Message); }
                Refresh();
            };
            Children.Add(add);
        }

        for (int i = 0; i < _cells.Count && i < desktops.Count; i++)
            Paint(_cells[i], desktops[i]);
    }

    Border MakeCell(DesktopInfo desktop)
    {
        var label = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.Normal,
            Foreground = Ui.PanelText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(_vertical ? 4 : 10, 0, _vertical ? 4 : 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        var underline = new Border
        {
            Height = _vertical ? double.NaN : 2,
            Width = _vertical ? 2 : double.NaN,
            HorizontalAlignment = _vertical ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            VerticalAlignment = _vertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        var host = new Grid();
        host.Children.Add(label);
        host.Children.Add(underline);
        var cell = new Border
        {
            MinWidth = _vertical ? 20 : 28,
            MaxWidth = _vertical ? 48 : 88,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = _vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true,
            Cursor = Cursors.Hand,
            Tag = desktop.Id,
            ToolTip = desktop.Name,
            Child = host
        };
        cell.MouseEnter += (_, _) => Shade(cell);
        cell.MouseLeave += (_, _) => Shade(cell);
        cell.MouseLeftButtonDown += (_, e) =>
        {
            Shade(cell);
            e.Handled = true;
        };
        cell.MouseLeftButtonUp += (_, e) =>
        {
            if (cell.Tag is Guid id)
            {
                try { VirtualDesktopService.Instance.SwitchTo(id); }
                catch (Exception ex) { Dialogs.Alert(_actions.Owner(), ex.Message); }
            }
            Shade(cell);
            e.Handled = true;
        };
        cell.MouseRightButtonUp += (_, e) =>
        {
            if (cell.Tag is Guid id) ShowMenu(id, cell);
            e.Handled = true;
        };
        return cell;
    }

    void Paint(Border cell, DesktopInfo desktop)
    {
        cell.Tag = desktop.Id;
        cell.ToolTip = desktop.Name;
        cell.BorderThickness = new Thickness(0);
        if (cell.Child is Grid grid && grid.Children[0] is TextBlock label)
        {
            label.Text = desktop.Badge;
            label.Foreground = Ui.PanelText();
        }
        Shade(cell);
    }

    void Shade(Border cell)
    {
        bool current = cell.Tag is Guid id && _desktops.Any(d => d.Id == id && d.IsCurrent);
        bool hover = cell.IsMouseOver;
        bool pressed = hover && Mouse.LeftButton == MouseButtonState.Pressed;
        cell.Background = pressed ? Ui.PanelPressed()
            : current || hover ? Ui.PanelCurrent()
            : Brushes.Transparent;
        if (cell.Child is Grid grid && grid.Children[1] is Border underline)
            underline.Background = current ? Ui.Accent() : Brushes.Transparent;
    }

    void ShowMenu(Guid id, FrameworkElement anchor)
    {
        var desktop = _desktops.FirstOrDefault(d => d.Id == id);
        var menu = new ContextMenu();
        menu.Items.Add(Item("Switch", () => VirtualDesktopService.Instance.SwitchTo(id)));
        menu.Items.Add(Item("Rename…", () =>
        {
            string? name = Dialogs.Prompt(_actions.Owner(), "Rename desktop", desktop?.Name ?? "");
            if (name == null) return;
            try { VirtualDesktopService.Instance.Rename(id, name); }
            catch (Exception ex) { Dialogs.Alert(_actions.Owner(), ex.Message); }
            _signature = "";
            Refresh();
        }));
        menu.Items.Add(Item("Remove", () =>
        {
            try { VirtualDesktopService.Instance.Remove(id); }
            catch (Exception ex) { Dialogs.Alert(_actions.Owner(), ex.Message); }
            _signature = "";
            Refresh();
        }));
        menu.Items.Add(Item("Add desktop", () =>
        {
            try { VirtualDesktopService.Instance.Create(); }
            catch (Exception ex) { Dialogs.Alert(_actions.Owner(), ex.Message); }
            _signature = "";
            Refresh();
        }));
        ContextMenus.Show(menu, anchor);
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_desktops.Count == 0) return;
        int index = 0;
        for (int i = 0; i < _desktops.Count; i++)
            if (_desktops[i].IsCurrent) index = i;
        int next = index + (e.Delta < 0 ? 1 : -1);
        if (next < 0 || next >= _desktops.Count) return;
        try { VirtualDesktopService.Instance.SwitchTo(_desktops[next].Id); }
        catch (Exception ex) { Log.Error("switch", ex); }
        e.Handled = true;
    }

    static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
