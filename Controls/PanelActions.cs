using System.Windows;
using WindowsTopMenuBar.Models;

namespace WindowsTopMenuBar.Controls;

public sealed class PanelActions
{
    public required Action<PanelItem> Remove { get; init; }
    public required Action<PanelItem, int> Move { get; init; }
    public required Action<FrameworkElement> ToggleMenu { get; init; }
    public required Action OpenSettings { get; init; }
    public required Action<PanelItem> ChangeLauncher { get; init; }
    public required Action<string, string> PinPath { get; init; }
    public required Func<bool> IsLocked { get; init; }
    public required Action ToggleLock { get; init; }
    public required Action Quit { get; init; }
    public required Func<Window> Owner { get; init; }
}
