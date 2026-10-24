using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsWhisker.Native;
using WindowsWhisker.Services;

namespace WindowsWhisker.Controls;

public static class Ui
{
    public const string AppDragFormat = "WindowsWhisker.App";

    static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly SolidColorBrush Text = Freeze("#E8E8EE");
    public static readonly SolidColorBrush Muted = Freeze("#A4A7B5");
    public static readonly SolidColorBrush Menu = Freeze("#2B2D36");
    public static readonly SolidColorBrush Field = Freeze("#1A1C22");
    public static readonly SolidColorBrush Line = Freeze("#3E4252");
    public static readonly SolidColorBrush Window = Freeze("#2C2E33");
    public static readonly SolidColorBrush Hover = Freeze("#353846");

    public static SolidColorBrush Brush(string? hex, string fallback = "#3B78E0")
    {
        string key = string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim();
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var brush = new SolidColorBrush(Parse(key, Parse(fallback, Colors.DodgerBlue)));
        brush.Freeze();
        Cache[key] = brush;
        return brush;
    }

    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color color) return color;
        }
        catch
        {
            // Keep the fallback color.
        }
        return fallback;
    }

    public static SolidColorBrush Accent() => Brush(SettingsStore.Instance.Config.Panel.Accent, "#3B78E0");

    public static SolidColorBrush PanelBackground() => Brush(SettingsStore.Instance.Config.Panel.Background, "#283851");

    public static SolidColorBrush PanelCurrent() => ShadePanel(12, 11, 15);

    public static SolidColorBrush PanelPressed() => ShadePanel(22, 20, 28);

    static SolidColorBrush ShadePanel(int r, int g, int b)
    {
        var color = PanelBackground().Color;
        return Brush($"#{Math.Max(0, color.R - r):X2}{Math.Max(0, color.G - g):X2}{Math.Max(0, color.B - b):X2}");
    }

    public static SolidColorBrush PanelText() =>
        SettingsStore.Instance.Config.Panel.DarkMode ? Text : Brush("#1B1D24");

    public static double PanelIcon()
    {
        var panel = SettingsStore.Instance.Config.Panel;
        if (!panel.AutoIconSize) return 18;
        return Math.Clamp(panel.Height - 12, 16, 48);
    }

    static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static FrameworkElement Mark(double size)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Border
        {
            Background = Accent(),
            CornerRadius = new CornerRadius(Math.Max(2, size * 0.18))
        });
        grid.Children.Add(new TextBlock
        {
            Text = "W",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size * 0.62,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0)
        });
        return grid;
    }

    public static FrameworkElement IconOrMark(string? path, double size, int index = 0)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var image = IconService.Get(path, index, (int)Math.Ceiling(size));
            if (image != null)
            {
                return new Image
                {
                    Source = image,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform
                };
            }
        }
        return Mark(size);
    }

    public static Button Flat()
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(1, 3, 1, 3),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        if (Application.Current?.TryFindResource("FlatButton") is Style style)
            button.Style = style;
        return button;
    }

    public static string UserName()
    {
        var sb = new System.Text.StringBuilder(256);
        int size = sb.Capacity;
        if (Win32.GetUserNameEx(3, sb, ref size) != 0 && sb.Length > 0)
            return sb.ToString();
        return Environment.UserName;
    }

    public static string Glyph(string kind) => kind switch
    {
        "settings" => "\uE713",
        "lock" => "\uE72E",
        "sleep" => "\uE823",
        "logoff" => "\uE77B",
        "restart" => "\uE777",
        "shutdown" => "\uE7E8",
        "quit" => "\uE8BB",
        "favorites" => "\uE734",
        "recent" => "\uE823",
        "all" => "\uE71D",
        "folder" => "\uE8B7",
        "search" => "\uE721",
        _ => "\uE756"
    };

    public static TextBlock GlyphText(string kind, double size) => new()
    {
        Text = Glyph(kind),
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = size,
        Foreground = Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static string MenuLabel(string text) => (text ?? "").Replace("_", "__");

    public static void DarkTitle(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Win32.UseDarkTitleBar(hwnd);
        };
    }
}
