using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsTopMenuBar.Models;
using WindowsTopMenuBar.Native;

namespace WindowsTopMenuBar.Services;

public sealed class WindowTracker
{
    public static WindowTracker Instance { get; } = new();

    static readonly HashSet<string> SkipClasses = new(StringComparer.Ordinal)
    {
        "Progman", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "ForegroundStaging", "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow", "ApplicationManager_DesktopShellWindow",
        "Windows.Internal.Shell.TabShellWindow", "ScreenClippingHost"
    };

    static readonly HashSet<string> SkipProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ScreenClippingHost", "SnippingTool", "ScreenSketch", "PickerHost",
        "ApplicationFrameHost", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "LockApp", "dwm", "conhost"
    };

    readonly Dictionary<IntPtr, ImageSource> _icons = new();
    readonly List<IntPtr> _hooks = [];
    readonly WinEventDelegate _hookProc;
    readonly DispatcherTimer _timer;
    readonly DispatcherTimer _debounce;
    readonly HashSet<string> _liveProcesses = new(StringComparer.OrdinalIgnoreCase);
    List<WindowInfo> _windows = [];
    bool _started;

    public event Action? Changed;
    public event Action<string>? ProcessAppeared;
    public IReadOnlyList<WindowInfo> Windows => _windows;

    WindowTracker()
    {
        _hookProc = OnWinEvent;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, flags));
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_OBJECT_NAMECHANGE, Win32.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _hookProc, 0, 0, flags));
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_OBJECT_CREATE, Win32.EVENT_OBJECT_HIDE, IntPtr.Zero, _hookProc, 0, 0, flags));
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_OBJECT_CLOAKED, Win32.EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _hookProc, 0, 0, flags));
        _timer.Start();
        Refresh();
    }

    public void Stop()
    {
        _timer.Stop();
        foreach (var hook in _hooks)
            if (hook != IntPtr.Zero) Win32.UnhookWinEvent(hook);
        _hooks.Clear();
    }

    public void Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (Win32.GetForegroundWindow() == hwnd && !Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);
            return;
        }
        Win32.ForceForeground(hwnd);
    }

    public void Close(IntPtr hwnd) => Win32.PostMessage(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public void Minimize(IntPtr hwnd) => Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);

    public void Maximize(IntPtr hwnd)
    {
        Win32.ShowWindow(hwnd, Win32.IsZoomed(hwnd) ? Win32.SW_RESTORE : Win32.SW_SHOWMAXIMIZED);
    }

    void OnWinEvent(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (!_debounce.IsEnabled) _debounce.Start();
        });
    }

    void Refresh()
    {
        try
        {
            RefreshCore();
        }
        catch (Exception ex)
        {
            Log.Error("window refresh", ex);
        }
    }

    void RefreshCore()
    {
        var found = new List<WindowInfo>();
        var foreground = Win32.GetForegroundWindow();
        uint self = (uint)Environment.ProcessId;
        Win32.EnumWindows((hwnd, _) =>
        {
            try { Collect(hwnd, self, foreground, found); }
            catch (Exception ex) { Log.Error("window collect", ex); }
            return true;
        }, IntPtr.Zero);

        var live = new Dictionary<IntPtr, WindowInfo>();
        foreach (var window in found)
            live[window.Handle] = window;
        var ordered = new List<WindowInfo>(_windows.Count + found.Count);
        foreach (var existing in _windows)
        {
            if (live.TryGetValue(existing.Handle, out var current))
                ordered.Add(current);
        }
        var kept = ordered.Select(w => w.Handle).ToHashSet();
        foreach (var window in found)
        {
            if (kept.Add(window.Handle))
                ordered.Add(window);
        }
        _windows = ordered;
        foreach (var key in _icons.Keys.Where(k => !live.ContainsKey(k)).ToList())
            _icons.Remove(key);
        NoteAppearedProcesses(ordered);
        Changed?.Invoke();
    }

    void NoteAppearedProcesses(List<WindowInfo> windows)
    {
        var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            if (!string.IsNullOrWhiteSpace(window.ProcessPath))
                now.Add(window.ProcessPath);
        }
        var appeared = new List<string>();
        foreach (var path in now)
        {
            if (_liveProcesses.Add(path))
                appeared.Add(path);
        }
        _liveProcesses.RemoveWhere(path => !now.Contains(path));
        if (appeared.Count == 0 || ProcessAppeared == null) return;
        string foreground = windows.FirstOrDefault(w => w.IsForeground)?.ProcessPath ?? "";
        foreach (var path in appeared)
        {
            if (!path.Equals(foreground, StringComparison.OrdinalIgnoreCase))
                ProcessAppeared.Invoke(path);
        }
        if (!string.IsNullOrWhiteSpace(foreground) &&
            appeared.Any(p => p.Equals(foreground, StringComparison.OrdinalIgnoreCase)))
            ProcessAppeared.Invoke(foreground);
    }

    void Collect(IntPtr hwnd, uint self, IntPtr foreground, List<WindowInfo> found)
    {
        if (!IsTaskWindow(hwnd, self)) return;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string? path = Win32.QueryProcessPath(pid);
        string processName = "";
        try { if (!string.IsNullOrWhiteSpace(path)) processName = Path.GetFileNameWithoutExtension(path); } catch { }
        var rect = new RECT();
        Win32.GetWindowRect(hwnd, out rect);
        if (!_icons.TryGetValue(hwnd, out var icon))
        {
            icon = ReadIcon(hwnd, path);
            if (icon != null) _icons[hwnd] = icon;
        }
        found.Add(new WindowInfo
        {
            Handle = hwnd,
            Title = Win32.GetWindowText(hwnd),
            ProcessPath = path ?? "",
            ProcessName = processName,
            IsForeground = hwnd == foreground,
            IsMinimized = Win32.IsIconic(hwnd),
            DesktopId = VirtualDesktopService.Instance.GetWindowDesktopId(hwnd),
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
            Icon = icon
        });
    }

    static bool IsTaskWindow(IntPtr hwnd, uint self)
    {
        if (!Win32.IsWindowVisible(hwnd)) return false;
        if (Win32.IsCloaked(hwnd)) return false;
        int ex = (int)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return false;
        IntPtr owner = Win32.GetWindow(hwnd, Win32.GW_OWNER);
        if (owner != IntPtr.Zero && (ex & Win32.WS_EX_APPWINDOW) == 0) return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == self) return false;
        var cls = Win32.GetClassName(hwnd);
        if (SkipClasses.Contains(cls)) return false;
        string process = "";
        try
        {
            string? path = Win32.QueryProcessPath(pid);
            if (!string.IsNullOrWhiteSpace(path))
                process = Path.GetFileNameWithoutExtension(path);
        }
        catch { }
        if (SkipProcesses.Contains(process)) return false;
        var title = Win32.GetWindowText(hwnd);
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (title == "Program Manager") return false;
        return true;
    }

    static ImageSource? ReadIcon(IntPtr hwnd, string? path)
    {
        try
        {
            IntPtr hIcon = Win32.SendMessageTimed(hwnd, Win32.WM_GETICON, (IntPtr)Win32.ICON_BIG, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = Win32.SendMessageTimed(hwnd, Win32.WM_GETICON, (IntPtr)Win32.ICON_SMALL2, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = Win32.SendMessageTimed(hwnd, Win32.WM_GETICON, (IntPtr)Win32.ICON_SMALL, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = Win32.GetClassLongPtr(hwnd, Win32.GCL_HICONSM);
            if (hIcon == IntPtr.Zero)
                hIcon = Win32.GetClassLongPtr(hwnd, Win32.GCL_HICON);
            if (hIcon != IntPtr.Zero)
            {
                var image = IconService.FromHIconShared(hIcon);
                if (image != null) return image;
            }
            if (!string.IsNullOrWhiteSpace(path))
                return IconService.Get(path, 0, 32);
        }
        catch (Exception ex)
        {
            Log.Error("window icon", ex);
        }
        return null;
    }
}
