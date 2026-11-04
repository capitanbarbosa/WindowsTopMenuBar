using System.Runtime.InteropServices;
using WindowsWhisker.Models;
using WindowsWhisker.Native;

namespace WindowsWhisker.Services;

public sealed class AppBarController
{
    uint _callback;
    IntPtr _hwnd;
    bool _registered;

    public bool IsRegistered => _registered;

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _callback = Win32.RegisterWindowMessage("WindowsWhisker.AppBar");
    }

    public int Consume(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_registered && msg == Win32.WM_ACTIVATE)
        {
            var data = Data();
            Win32.SHAppBarMessage(Win32.ABM_ACTIVATE, ref data);
        }
        else if (_registered && msg == Win32.WM_WINDOWPOSCHANGED)
        {
            var data = Data();
            Win32.SHAppBarMessage(Win32.ABM_WINDOWPOSCHANGED, ref data);
        }

        if (_callback != 0 && msg == (int)_callback)
            return wParam.ToInt32();
        return -1;
    }

    public bool TryApply(PanelConfig cfg, bool expanded, out RECT rect, string? device = null)
    {
        rect = default;
        if (_hwnd == IntPtr.Zero) return false;

        var monitors = Win32.GetMonitors();
        if (monitors.Count == 0) return false;

        string want = string.IsNullOrWhiteSpace(device) ? cfg.MonitorDevice : device;
        var mon = monitors.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(want) &&
            string.Equals(m.Device, want, StringComparison.OrdinalIgnoreCase));
        if (mon.Handle == IntPtr.Zero)
            mon = monitors.FirstOrDefault(m => m.Primary);
        if (mon.Handle == IntPtr.Zero)
            mon = monitors[0];

        uint edge = cfg.Edge switch
        {
            "Bottom" => Win32.ABE_BOTTOM,
            "Left" => Win32.ABE_LEFT,
            "Right" => Win32.ABE_RIGHT,
            _ => Win32.ABE_TOP
        };

        uint dpi = Win32.GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        int thickness = (int)Math.Round(cfg.ThicknessDip * dpi / 96.0);
        if (cfg.AutoHide && !expanded)
            thickness = Math.Max(2, (int)Math.Round(3 * dpi / 96.0));
        thickness = Math.Max(2, thickness);

        bool reserve = cfg.ReserveSpace && !cfg.AutoHide;
        if (reserve)
        {
            if (!_registered) Register();
            var data = Data();
            data.uEdge = edge;
            data.rc = mon.Bounds;
            Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref data);
            Fit(ref data.rc, edge, thickness);
            data.rc = LimitLength(data.rc, cfg, edge, dpi);
            Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref data);
            rect = data.rc;
            Place(rect, topmost: false);
            return rect.Width > 0 && rect.Height > 0;
        }

        if (_registered) Unregister();
        rect = mon.Bounds;
        Fit(ref rect, edge, thickness);
        rect = LimitLength(rect, cfg, edge, dpi);
        Place(rect, topmost: true);
        return true;
    }

    static RECT LimitLength(RECT rc, PanelConfig cfg, uint edge, uint dpi)
    {
        if (cfg.AutoLength) return rc;
        int length = (int)Math.Round(Math.Clamp(cfg.Length, 80, 8000) * dpi / 96.0);
        if (edge is Win32.ABE_TOP or Win32.ABE_BOTTOM)
        {
            int width = Math.Clamp(length, 40, Math.Max(40, rc.Width));
            rc.Right = rc.Left + width;
        }
        else
        {
            int height = Math.Clamp(length, 40, Math.Max(40, rc.Height));
            rc.Bottom = rc.Top + height;
        }
        return rc;
    }

    public void Unregister()
    {
        if (!_registered || _hwnd == IntPtr.Zero) return;
        var data = Data();
        Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref data);
        _registered = false;
    }

    void Register()
    {
        var data = Data();
        Win32.SHAppBarMessage(Win32.ABM_NEW, ref data);
        _registered = true;
    }

    void Place(RECT rc, bool topmost)
    {
        Win32.SetWindowPos(
            _hwnd,
            topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
            rc.Left,
            rc.Top,
            Math.Max(1, rc.Width),
            Math.Max(1, rc.Height),
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    static void Fit(ref RECT rc, uint edge, int thickness)
    {
        switch (edge)
        {
            case Win32.ABE_TOP:
                rc.Bottom = rc.Top + thickness;
                break;
            case Win32.ABE_BOTTOM:
                rc.Top = rc.Bottom - thickness;
                break;
            case Win32.ABE_LEFT:
                rc.Right = rc.Left + thickness;
                break;
            default:
                rc.Left = rc.Right - thickness;
                break;
        }
    }

    APPBARDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
        uCallbackMessage = _callback
    };
}
