using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowsTopMenuBar.Native;

namespace WindowsTopMenuBar.Controls;

public static class ContextMenus
{
    const int VkLButton = 0x01;
    const int VkRButton = 0x02;
    const int VkMButton = 0x04;

    static ContextMenu? _open;
    static IntPtr _popupHwnd;
    static bool _armed;
    static readonly DispatcherTimer Poll = CreatePoll();

    static DispatcherTimer CreatePoll()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => PollClickAway();
        return timer;
    }

    public static void Show(ContextMenu menu, FrameworkElement? target = null, PlacementMode placement = PlacementMode.MousePoint)
    {
        if (_open != null && _open != menu)
            _open.IsOpen = false;

        menu.StaysOpen = false;
        menu.HasDropShadow = false;
        menu.Placement = placement;
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
        _armed = false;
        if (sender is ContextMenu menu)
        {
            var source = PresentationSource.FromVisual(menu) as HwndSource;
            _popupHwnd = source?.Handle ?? IntPtr.Zero;
        }
        Poll.Stop();
        Poll.Start();
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
        _armed = false;
        Poll.Stop();
    }

    static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (sender is ContextMenu menu)
            menu.IsOpen = false;
        e.Handled = true;
    }

    static void PollClickAway()
    {
        if (_open == null)
        {
            Poll.Stop();
            return;
        }

        bool down = Down(VkLButton) || Down(VkRButton) || Down(VkMButton);
        if (!down)
        {
            _armed = true;
            return;
        }
        if (!_armed) return;
        if (!Win32.GetCursorPos(out var pt)) return;
        if (HitsPopup(pt)) return;
        _open.IsOpen = false;
    }

    static bool Down(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    static bool HitsPopup(POINT pt)
    {
        if (_popupHwnd == IntPtr.Zero) return false;
        IntPtr hwnd = Win32.WindowFromPoint(pt);
        if (hwnd == _popupHwnd) return true;
        IntPtr root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        if (root == _popupHwnd) return true;

        IntPtr walk = root != IntPtr.Zero ? root : hwnd;
        for (int i = 0; i < 8 && walk != IntPtr.Zero; i++)
        {
            IntPtr owner = Win32.GetWindow(walk, Win32.GW_OWNER);
            if (owner == _popupHwnd) return true;
            if (owner == IntPtr.Zero || owner == walk) break;
            walk = owner;
        }
        return false;
    }
}
