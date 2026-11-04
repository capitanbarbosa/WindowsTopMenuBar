using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowsWhisker.Native;

namespace WindowsWhisker.Controls;

public static class ContextMenus
{
    static ContextMenu? _open;
    static IntPtr _hook;
    static IntPtr _popupHwnd;
    static readonly HookProc Proc = OnMouse;
    static bool _ignoreNextRightUp;

    public static void Show(ContextMenu menu, FrameworkElement? target = null)
    {
        if (_open != null && _open != menu)
            _open.IsOpen = false;

        menu.StaysOpen = false;
        if (target != null)
            menu.PlacementTarget = target;
        menu.Closed -= OnClosed;
        menu.Closed += OnClosed;
        menu.Opened -= OnOpened;
        menu.Opened += OnOpened;
        menu.PreviewKeyDown -= OnKey;
        menu.PreviewKeyDown += OnKey;
        _open = menu;
        menu.IsOpen = true;
    }

    static void OnOpened(object sender, RoutedEventArgs e)
    {
        _ignoreNextRightUp = Mouse.RightButton == MouseButtonState.Pressed;
        if (sender is ContextMenu menu)
        {
            var source = PresentationSource.FromVisual(menu) as HwndSource;
            _popupHwnd = source?.Handle ?? IntPtr.Zero;
        }
        if (_hook != IntPtr.Zero) return;
        IntPtr module = Win32.GetModuleHandle(null);
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, Proc, module, 0);
        if (_hook == IntPtr.Zero)
            _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, Proc, IntPtr.Zero, 0);
    }

    static void OnClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnClosed;
            menu.Opened -= OnOpened;
            menu.PreviewKeyDown -= OnKey;
        }
        if (_open == sender)
            _open = null;
        _popupHwnd = IntPtr.Zero;
        DropHook();
    }

    static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (sender is ContextMenu menu)
            menu.IsOpen = false;
        e.Handled = true;
    }

    static void DropHook()
    {
        if (_hook == IntPtr.Zero) return;
        Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        IntPtr hook = _hook;
        try
        {
            if (nCode >= 0 && _open != null)
            {
                int msg = wParam.ToInt32();
                if (_ignoreNextRightUp && msg == Win32.WM_RBUTTONUP)
                    _ignoreNextRightUp = false;
                else if (msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_NCLBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (!HitsPopup(info.pt))
                        DismissLater();
                }
            }
        }
        catch
        {
            // A throw inside a low-level hook takes the process down.
        }
        return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
    }

    static bool HitsPopup(POINT pt)
    {
        if (_popupHwnd == IntPtr.Zero) return false;
        IntPtr hwnd = Win32.WindowFromPoint(pt);
        while (hwnd != IntPtr.Zero)
        {
            if (hwnd == _popupHwnd) return true;
            IntPtr owner = Win32.GetWindow(hwnd, Win32.GW_OWNER);
            if (owner == _popupHwnd) return true;
            hwnd = Win32.GetParent(hwnd);
            if (hwnd == IntPtr.Zero && owner != IntPtr.Zero)
                hwnd = owner;
        }
        return false;
    }

    static void DismissLater()
    {
        var menu = _open;
        if (menu == null) return;
        menu.Dispatcher.BeginInvoke(() =>
        {
            if (menu.IsOpen)
                menu.IsOpen = false;
        }, DispatcherPriority.Input);
    }
}
