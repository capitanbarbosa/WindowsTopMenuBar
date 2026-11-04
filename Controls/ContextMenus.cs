using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsWhisker.Native;

namespace WindowsWhisker.Controls;

public static class ContextMenus
{
    static ContextMenu? _open;
    static IntPtr _hook;
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
        if (nCode >= 0 && _open != null)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32.WM_RBUTTONDOWN || msg == Win32.WM_RBUTTONUP)
            {
                if (_ignoreNextRightUp && msg == Win32.WM_RBUTTONUP)
                {
                    _ignoreNextRightUp = false;
                    return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
                }
            }
            if (msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_NCLBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (!HitsPopup(info.pt))
                    Dismiss();
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static bool HitsPopup(POINT pt)
    {
        IntPtr hwnd = Win32.WindowFromPoint(pt);
        while (hwnd != IntPtr.Zero)
        {
            try
            {
                var source = HwndSource.FromHwnd(hwnd);
                if (source?.RootVisual != null)
                {
                    string name = source.RootVisual.GetType().Name;
                    if (name is "PopupRoot" || source.RootVisual is ContextMenu)
                        return true;
                }
            }
            catch
            {
                // Native window; keep walking parents.
            }
            hwnd = Win32.GetParent(hwnd);
        }
        return false;
    }

    static void Dismiss()
    {
        var menu = _open;
        if (menu == null) return;
        var dispatcher = menu.Dispatcher;
        if (dispatcher.CheckAccess())
            menu.IsOpen = false;
        else
            dispatcher.BeginInvoke(() => menu.IsOpen = false);
    }
}
