using System.Text.Json.Serialization;

namespace WindowsWhisker.Models;

public static class PanelKinds
{
    public const string Menu = "Menu";
    public const string Launcher = "Launcher";
    public const string TaskList = "TaskList";
    public const string DesktopShifter = "DesktopShifter";
    public const string Clock = "Clock";
    public const string Separator = "Separator";
    public const string Spacer = "Spacer";
    public const string ShowDesktop = "ShowDesktop";
    public const string Clipboard = "Clipboard";
    public const string Settings = "Settings";
    public const string CpuGraph = "CpuGraph";
}

public sealed record PanelPlugin(string Kind, string Name, string Description, string Glyph);

public static class PanelPlugins
{
    public static IReadOnlyList<PanelPlugin> All { get; } =
    [
        new(PanelKinds.Launcher, "Launcher", "Program launcher. Pick an application to pin on the bar.", "\uE7C3"),
        new(PanelKinds.Separator, "Separator", "A line that expands to fill the space between panel items.", "\uE738"),
        new(PanelKinds.Spacer, "Spacer", "Adds an empty space between panel items.", "\uE76F"),
        new(PanelKinds.Clipboard, "Clipboard", "Shortcut that opens Windows clipboard history.", "\uE8C8"),
        new(PanelKinds.Menu, "Applications Menu", "The Whisker-style application menu.", "\uE700"),
        new(PanelKinds.Clock, "Clock", "Shows the date and time.", "\uE787"),
        new(PanelKinds.Settings, "Settings", "Opens panel and menu settings.", "\uE713"),
        new(PanelKinds.DesktopShifter, "Workspace Switcher", "Switch between virtual desktops.", "\uE7C1"),
        new(PanelKinds.TaskList, "Window Buttons", "An icon for each open window. Hover for the title.", "\uE737"),
        new(PanelKinds.ShowDesktop, "Show Desktop", "Hides windows and shows the desktop.", "\uE7F4"),
        new(PanelKinds.CpuGraph, "CPU Graph", "A Kali-style meter of recent CPU usage.", "\uE9D2")
    ];

    public static string GlyphFor(string kind) =>
        All.FirstOrDefault(p => p.Kind == kind)?.Glyph ?? "\uE756";
}

public enum ConfigArea
{
    PanelGeometry,
    PanelItems,
    PanelChrome,
    Menu
}

public sealed class AppConfig
{
    public PanelConfig Panel { get; set; } = new();
    public MenuConfig Menu { get; set; } = new();
    public List<string> Favorites { get; set; } = [];
    public List<string> Recent { get; set; } = [];
    public bool RunAtStartup { get; set; }
    public List<PanelProfile> Profiles { get; set; } = [];
    public string ActiveProfile { get; set; } = "Panel 1";
}

public sealed class PanelConfig
{
    public const string DefaultBackground = "#17191F";
    public const string DefaultAccent = "#3B78E0";

    public int Height { get; set; } = 34;
    public int Rows { get; set; } = 1;
    public int BorderWidth { get; set; }
    public int Length { get; set; } = 1400;
    public bool AutoLength { get; set; } = true;
    public string Mode { get; set; } = "Horizontal";
    public string Edge { get; set; } = "Top";
    public bool Locked { get; set; } = true;
    public bool ReserveSpace { get; set; } = true;
    public bool AutoHide { get; set; }
    public string AutoHideMode { get; set; } = "Never";
    public string MonitorDevice { get; set; } = "";
    public bool ShowOnAllDisplays { get; set; } = true;
    public bool DarkMode { get; set; } = true;
    public string BackgroundStyle { get; set; } = "Solid";
    public string Background { get; set; } = DefaultBackground;
    public string Accent { get; set; } = DefaultAccent;
    public bool AutoIconSize { get; set; } = true;
    public int OpacityEnter { get; set; } = 100;
    public int OpacityLeave { get; set; } = 100;
    public List<PanelItem> Items { get; set; } = PanelItem.CreateDefault();

    public int ThicknessDip => Math.Clamp(Math.Clamp(Height, 16, 128) * Math.Clamp(Rows, 1, 6), 16, 256);
}

public sealed class PanelProfile
{
    public string Name { get; set; } = "Panel 1";
    public PanelConfig Panel { get; set; } = new();
}

public sealed class PanelItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = PanelKinds.Separator;
    public string Label { get; set; } = "";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
    public int IconIndex { get; set; }
    public string Style { get; set; } = "Separator";
    public bool Expand { get; set; } = true;

    public string Summary => Kind switch
    {
        PanelKinds.Menu => "Whisker Menu",
        PanelKinds.Launcher => string.IsNullOrWhiteSpace(Label) ? "Launcher" : Label,
        PanelKinds.TaskList => "Window Buttons",
        PanelKinds.DesktopShifter => "Workspace Switcher",
        PanelKinds.Clock => "Clock",
        PanelKinds.Separator => "Separator",
        PanelKinds.Spacer => "Spacer",
        PanelKinds.ShowDesktop => "Show Desktop",
        PanelKinds.Clipboard => "Clipboard",
        PanelKinds.Settings => "Settings",
        PanelKinds.CpuGraph => "CPU Graph",
        _ => Kind
    };

    [JsonIgnore]
    public string Glyph => PanelPlugins.GlyphFor(Kind);

    public const string PackedTerminalIcon = "pack://application:,,,/Assets/terminal.png";
    public const string PackedFilesIcon = "pack://application:,,,/Assets/folder_open.png";

    public static PanelItem Launcher(string target, string label, string? arguments = null, string? workingDirectory = null)
    {
        return new PanelItem
        {
            Kind = PanelKinds.Launcher,
            Target = target,
            Label = label,
            Arguments = arguments ?? "",
            WorkingDirectory = workingDirectory ?? "",
            IconPath = PackedIconFor(label) ?? ""
        };
    }

    public static string? PackedIconFor(string? label) => label switch
    {
        "Terminal" => PackedTerminalIcon,
        "Files" => PackedFilesIcon,
        _ => null
    };

    public string ResolveIconPath()
    {
        if (!string.IsNullOrWhiteSpace(IconPath))
        {
            string ext = Path.GetExtension(IconPath);
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                return IconPath;
        }
        return PackedIconFor(Label) ?? (string.IsNullOrWhiteSpace(IconPath) ? Target : IconPath);
    }

    public static List<PanelItem> CreateDefault()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        return
        [
            new() { Kind = PanelKinds.Menu },
            Launcher(FindTerminal(), "Terminal"),
            Launcher(explorer, "Files"),
            CompactSeparator(),
            new() { Kind = PanelKinds.DesktopShifter },
            CompactSeparator(),
            new() { Kind = PanelKinds.ShowDesktop },
            new() { Kind = PanelKinds.TaskList },
            new() { Kind = PanelKinds.Separator, Style = "Transparent", Expand = true },
            new() { Kind = PanelKinds.CpuGraph },
            new() { Kind = PanelKinds.Clock },
            new() { Kind = PanelKinds.Settings }
        ];
    }

    static PanelItem CompactSeparator() => new()
    {
        Kind = PanelKinds.Separator,
        Style = "Separator",
        Expand = false
    };

    static string FindTerminal()
    {
        var wt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wt))
            return wt;
        var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(ps) ? ps : "powershell.exe";
    }

}

public sealed class MenuConfig
{
    public string ViewMode { get; set; } = "List";
    public bool ShowGenericNames { get; set; }
    public bool ShowCategoryNames { get; set; } = true;
    public bool ShowTooltips { get; set; } = true;
    public bool ShowDescriptions { get; set; }
    public string AppIconSize { get; set; } = "Small";
    public string CategoryIconSize { get; set; } = "Smaller";
    public int MenuWidth { get; set; } = 570;
    public int MenuHeight { get; set; } = 700;
    public int BackgroundOpacity { get; set; } = 100;

    public bool CategoriesHorizontal { get; set; }
    public bool CategoriesOnLeft { get; set; } = true;
    public bool ProfileOnBottom { get; set; } = true;
    public bool SearchOnBottom { get; set; }
    public bool CommandsNextToSearch { get; set; }
    public string ProfileStyle { get; set; } = "Round";
    public string ButtonDisplay { get; set; } = "Icon";
    public string ButtonTitle { get; set; } = "Applications";
    public string ButtonIconPath { get; set; } = "";
    public bool SinglePanelRow { get; set; }

    public string DefaultCategory { get; set; } = "Favorites";
    public bool HoverSwitch { get; set; } = true;
    public bool StayVisibleOnFocusLoss { get; set; }
    public bool SortCategories { get; set; }
    public int RecentCount { get; set; } = 10;
    public bool RecentIncludeFavorites { get; set; }
    public bool ConfirmSessionCommands { get; set; } = true;
    public bool AltF1 { get; set; } = true;

    public bool SearchDescriptions { get; set; } = true;
    public bool SearchExecutables { get; set; } = true;

    public List<CommandItem> Commands { get; set; } = CommandItem.CreateDefault();

    public static int IconPx(string name) => name switch
    {
        "Smaller" => 16,
        "Small" => 24,
        "Medium" => 32,
        "Large" => 48,
        "Larger" => 64,
        _ => 24
    };
}

public sealed class CommandItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "command";
    public string CommandLine { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Confirm { get; set; }

    public static List<CommandItem> CreateDefault() =>
    [
        new() { Id = "settings", Name = "Settings", Kind = "settings", Confirm = false },
        new() { Id = "lock", Name = "Lock", Kind = "lock", Confirm = false },
        new() { Id = "sleep", Name = "Sleep", Kind = "sleep", Confirm = true },
        new() { Id = "logoff", Name = "Log Out", Kind = "logoff", Confirm = true },
        new() { Id = "restart", Name = "Restart", Kind = "restart", Confirm = true },
        new() { Id = "shutdown", Name = "Shut Down", Kind = "shutdown", Confirm = true }
    ];
}

public sealed class AppEntry
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string GenericName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "Applications";
    public string TargetPath { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string IconPath { get; init; } = "";
    public int IconIndex { get; init; }
    public string AppUserModelId { get; init; } = "";

    public string DisplayName(bool generic)
    {
        if (generic && !string.IsNullOrWhiteSpace(GenericName))
            return GenericName;
        return string.IsNullOrWhiteSpace(Name) ? GenericName : Name;
    }
}

public sealed class DesktopInfo
{
    public Guid Id { get; init; }
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public bool IsCurrent { get; init; }

    public string Badge
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return (Index + 1).ToString();
            if (Name.Equals("Desktop " + (Index + 1), StringComparison.OrdinalIgnoreCase))
                return (Index + 1).ToString();
            return Name.Trim();
        }
    }
}

public sealed class WindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public string ProcessPath { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public bool IsForeground { get; init; }
    public bool IsMinimized { get; init; }
    public Guid DesktopId { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public System.Windows.Media.ImageSource? Icon { get; init; }
}
