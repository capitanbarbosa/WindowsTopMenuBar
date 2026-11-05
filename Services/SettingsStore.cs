using System.Text.Json;
using WindowsTopMenuBar.Models;

namespace WindowsTopMenuBar.Services;

public sealed class SettingsStore
{
    public static SettingsStore Instance { get; } = new();

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    readonly string _path;
    readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    bool _dirty;

    public AppConfig Config { get; private set; } = new();
    public bool FirstRun { get; private set; }
    public event Action<ConfigArea>? AreaChanged;

    SettingsStore()
    {
        _path = Path.Combine(Log.DataFolder, "config.json");
        _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (_dirty) SaveNow();
        };
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            FirstRun = true;
            Config = new AppConfig();
            Normalize(Config);
            SaveNow();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Log.Error("config load failed", ex);
            try { File.Copy(_path, _path + ".bad", true); } catch { /* keep going with defaults */ }
            Config = new AppConfig();
        }
        Normalize(Config);
        if (EnsureSettingsShortcut(Config))
            ScheduleSave();
    }

    public void Update(Action<AppConfig> change, ConfigArea area)
    {
        change(Config);
        Normalize(Config);
        RememberActive();
        AreaChanged?.Invoke(area);
        ScheduleSave();
    }

    public void SwitchProfile(string name)
    {
        RememberActive();
        var next = Config.Profiles.FirstOrDefault(p => p.Name == name);
        if (next?.Panel == null) return;
        Config.ActiveProfile = next.Name;
        Config.Panel = ClonePanel(next.Panel);
        Normalize(Config);
        AreaChanged?.Invoke(ConfigArea.PanelGeometry);
        AreaChanged?.Invoke(ConfigArea.PanelItems);
        AreaChanged?.Invoke(ConfigArea.PanelChrome);
        ScheduleSave();
    }

    public void AddProfile()
    {
        RememberActive();
        int n = Config.Profiles.Count + 1;
        string name;
        do { name = "Panel " + n++; }
        while (Config.Profiles.Any(p => p.Name == name));
        Config.Profiles.Add(new PanelProfile { Name = name, Panel = ClonePanel(Config.Panel) });
        Config.ActiveProfile = name;
        ScheduleSave();
    }

    public void RemoveActiveProfile()
    {
        if (Config.Profiles.Count < 2) return;
        Config.Profiles.RemoveAll(p => p.Name == Config.ActiveProfile);
        var next = Config.Profiles[0];
        Config.ActiveProfile = next.Name;
        Config.Panel = ClonePanel(next.Panel);
        Normalize(Config);
        AreaChanged?.Invoke(ConfigArea.PanelGeometry);
        AreaChanged?.Invoke(ConfigArea.PanelItems);
        AreaChanged?.Invoke(ConfigArea.PanelChrome);
        ScheduleSave();
    }

    public string ExportProfiles()
    {
        RememberActive();
        return JsonSerializer.Serialize(new ProfileFile
        {
            ActiveProfile = Config.ActiveProfile,
            Profiles = Config.Profiles
        }, JsonOptions);
    }

    public void ImportProfiles(string json)
    {
        var file = JsonSerializer.Deserialize<ProfileFile>(json, JsonOptions);
        if (file?.Profiles == null || file.Profiles.Count == 0)
            throw new InvalidOperationException("That file has no panel profiles.");
        Config.Profiles = file.Profiles;
        Config.ActiveProfile = file.ActiveProfile;
        Normalize(Config);
        var active = Config.Profiles.First(p => p.Name == Config.ActiveProfile);
        Config.Panel = ClonePanel(active.Panel);
        Normalize(Config);
        AreaChanged?.Invoke(ConfigArea.PanelGeometry);
        AreaChanged?.Invoke(ConfigArea.PanelItems);
        AreaChanged?.Invoke(ConfigArea.PanelChrome);
        ScheduleSave();
    }

    void RememberActive()
    {
        var profile = Config.Profiles.FirstOrDefault(p => p.Name == Config.ActiveProfile);
        if (profile == null) return;
        profile.Panel = ClonePanel(Config.Panel);
    }

    public void ResetToDefaults()
    {
        var factory = new AppConfig();
        Config.Panel = ClonePanel(factory.Panel);
        Config.Menu = Clone(factory.Menu);
        Config.RunAtStartup = factory.RunAtStartup;
        RememberActive();
        Normalize(Config);
        AreaChanged?.Invoke(ConfigArea.PanelGeometry);
        AreaChanged?.Invoke(ConfigArea.PanelItems);
        AreaChanged?.Invoke(ConfigArea.PanelChrome);
        AreaChanged?.Invoke(ConfigArea.Menu);
        SaveNow();
        StartupService.Apply(Config.RunAtStartup);
    }

    static PanelConfig ClonePanel(PanelConfig panel) => Clone(panel);

    static T Clone<T>(T value) where T : class, new()
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    }

    sealed class ProfileFile
    {
        public string ActiveProfile { get; set; } = "Panel 1";
        public List<PanelProfile> Profiles { get; set; } = [];
    }

    public void SaveQuiet()
    {
        Normalize(Config);
        ScheduleSave();
    }

    public void NotifyItems()
    {
        Normalize(Config);
        AreaChanged?.Invoke(ConfigArea.PanelItems);
        ScheduleSave();
    }

    public void PushRecent(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        Config.Recent.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        Config.Recent.Insert(0, key);
        if (Config.Recent.Count > 50)
            Config.Recent.RemoveRange(50, Config.Recent.Count - 50);
        ScheduleSave();
    }

    public void RecordLaunch(string? path, string? aumid = null)
    {
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(aumid)) return;
        var app = AppCatalogService.Instance.FindByTarget(path, aumid);
        if (app != null)
        {
            PushRecent(app.Key);
            return;
        }
        if (!AppCatalogService.Instance.IsReady && !string.IsNullOrWhiteSpace(path))
            PushRecent(path);
    }

    public void ToggleFavorite(string key)
    {
        if (Config.Favorites.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) == 0)
            Config.Favorites.Add(key);
        AreaChanged?.Invoke(ConfigArea.Menu);
        ScheduleSave();
    }

    public bool IsFavorite(string key) =>
        Config.Favorites.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

    void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _dirty = false;
        try
        {
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, true);
        }
        catch (Exception ex)
        {
            Log.Error("config save failed", ex);
        }
    }

    static bool EnsureSettingsShortcut(AppConfig config)
    {
        if (config.Panel.Items.Any(i => i.Kind == PanelKinds.Settings))
            return false;
        int clock = config.Panel.Items.FindLastIndex(i => i.Kind == PanelKinds.Clock);
        var settings = new PanelItem { Kind = PanelKinds.Settings };
        if (clock >= 0) config.Panel.Items.Insert(clock + 1, settings);
        else config.Panel.Items.Add(settings);
        return true;
    }

    static void Normalize(AppConfig config)
    {
        config.Panel ??= new PanelConfig();
        config.Menu ??= new MenuConfig();
        config.Favorites ??= [];
        config.Recent ??= [];
        config.Panel.Items ??= PanelItem.CreateDefault();
        config.Menu.Commands = CommandItem.CreateDefault();
        config.Menu.ConfirmSessionCommands = true;
        config.Menu.SearchDescriptions = true;
        config.Menu.SearchExecutables = true;
        config.Menu.DefaultCategory = "Favorites";
        config.Menu.HoverSwitch = true;
        config.Menu.StayVisibleOnFocusLoss = false;
        config.Menu.SortCategories = false;
        config.Menu.RecentCount = 10;
        config.Menu.RecentIncludeFavorites = false;
        config.Menu.CategoriesHorizontal = false;
        config.Menu.CategoriesOnLeft = true;
        config.Menu.ProfileOnBottom = true;
        config.Menu.SearchOnBottom = false;
        config.Menu.CommandsNextToSearch = false;
        config.Profiles ??= [];
        config.Panel.Height = Math.Clamp(config.Panel.Height, 16, 128);
        config.Panel.Rows = Math.Clamp(config.Panel.Rows, 1, 6);
        config.Panel.BorderWidth = Math.Clamp(config.Panel.BorderWidth, 0, 20);
        config.Panel.Length = Math.Clamp(config.Panel.Length, 80, 8000);
        config.Panel.OpacityEnter = 100;
        config.Panel.OpacityLeave = 100;
        if (string.IsNullOrWhiteSpace(config.Panel.Mode)) config.Panel.Mode = "Horizontal";
        if (string.IsNullOrWhiteSpace(config.Panel.BackgroundStyle)) config.Panel.BackgroundStyle = "Solid";
        if (string.IsNullOrWhiteSpace(config.Panel.Text)) config.Panel.Text = PanelConfig.DefaultText;
        if (string.IsNullOrWhiteSpace(config.Panel.AutoHideMode))
            config.Panel.AutoHideMode = config.Panel.AutoHide ? "Always" : "Never";
        config.Panel.AutoHide = config.Panel.AutoHideMode is "Always" or "Intelligent";
        if (config.Panel.Edge is "Left" or "Right")
        {
            if (config.Panel.Mode == "Horizontal")
                config.Panel.Mode = "Vertical";
        }
        else if (config.Panel.Mode is "Vertical" or "Deskbar")
            config.Panel.Mode = "Horizontal";
        config.Menu.ViewMode = "List";
        config.Menu.ShowGenericNames = false;
        config.Menu.ShowCategoryNames = true;
        config.Menu.ShowTooltips = true;
        config.Menu.ShowDescriptions = false;
        config.Menu.AppIconSize = "Small";
        config.Menu.CategoryIconSize = "Smaller";
        config.Menu.MenuWidth = 570;
        config.Menu.MenuHeight = 700;
        config.Menu.BackgroundOpacity = 100;
        foreach (var item in config.Panel.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                item.Id = Guid.NewGuid().ToString("N");
            item.Label ??= "";
            item.Target ??= "";
            item.Arguments ??= "";
            item.WorkingDirectory ??= "";
            item.IconPath ??= "";
            item.Kind ??= PanelKinds.Separator;
            if (item.Kind == PanelKinds.Separator)
            {
                if (item.Style is not ("Separator" or "Transparent" or "Handle" or "Dots"))
                    item.Style = "Separator";
            }
        }
        if (config.Panel.AutoHide)
            config.Panel.ReserveSpace = false;
        if (config.Profiles.Count == 0)
        {
            config.Profiles.Add(new PanelProfile { Name = "Panel 1", Panel = ClonePanel(config.Panel) });
            config.ActiveProfile = "Panel 1";
        }
        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = "Panel";
            profile.Panel ??= new PanelConfig();
        }
        if (string.IsNullOrWhiteSpace(config.ActiveProfile) || config.Profiles.All(p => p.Name != config.ActiveProfile))
            config.ActiveProfile = config.Profiles[0].Name;
    }
}
