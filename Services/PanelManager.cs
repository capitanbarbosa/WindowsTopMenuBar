using System.Windows;
using System.Windows.Threading;
using WindowsWhisker.Models;
using WindowsWhisker.Native;
using WindowsWhisker.Views;

namespace WindowsWhisker.Services;

public sealed class PanelManager
{
    public static PanelManager Instance { get; } = new();

    readonly List<PanelWindow> _panels = [];
    readonly DispatcherTimer _debounce;
    SettingsWindow? _settings;
    bool _syncing;
    bool _started;

    PanelManager()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Sync();
        };
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        SettingsStore.Instance.AreaChanged += OnConfig;
        Sync();
    }

    public void NotifyDisplays()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void PrepareExit()
    {
        foreach (var panel in _panels)
            panel.AllowClose();
    }

    public void QuitAll()
    {
        PrepareExit();
        foreach (var panel in _panels.ToList())
            panel.Close();
        _panels.Clear();
        Application.Current.Shutdown();
    }

    public void OpenSettings(string tab)
    {
        if (_settings != null)
        {
            _settings.SelectTab(tab);
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(tab);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    public void ToggleMenuAtCursor()
    {
        Win32.GetCursorPos(out var pt);
        var panel = _panels.FirstOrDefault(p => p.ContainsScreenPoint(pt)) ?? _panels.FirstOrDefault();
        panel?.ToggleMenuFromHotkey();
    }

    void OnConfig(ConfigArea area)
    {
        if (area == ConfigArea.PanelGeometry)
            NotifyDisplays();
    }

    void Sync()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var monitors = Win32.GetMonitors();
            var cfg = SettingsStore.Instance.Config.Panel;
            var wanted = WantedDevices(monitors, cfg);

            foreach (var panel in _panels.ToList())
            {
                if (wanted.Any(d => d.Equals(panel.MonitorDevice, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _panels.Remove(panel);
                panel.AllowClose();
                panel.Close();
            }

            bool needHotkey = _panels.TrueForAll(p => !p.OwnsHotkey);
            foreach (var device in wanted)
            {
                if (_panels.Any(p => p.MonitorDevice.Equals(device, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var panel = new PanelWindow(device, needHotkey);
                _panels.Add(panel);
                panel.Show();
                needHotkey = false;
            }

            if (_panels.Count > 0 && _panels.TrueForAll(p => !p.OwnsHotkey))
                _panels[0].SetOwnsHotkey(true);

            if (_panels.Count > 0)
                Application.Current.MainWindow = _panels[0];
        }
        catch (Exception ex)
        {
            Log.Error("panel sync", ex);
        }
        finally
        {
            _syncing = false;
        }
    }

    static List<string> WantedDevices(IReadOnlyList<MonitorDesc> monitors, PanelConfig cfg)
    {
        if (monitors.Count == 0) return [];
        if (cfg.ShowOnAllDisplays)
            return monitors.Select(m => m.Device).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var mon = monitors.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(cfg.MonitorDevice) &&
            m.Device.Equals(cfg.MonitorDevice, StringComparison.OrdinalIgnoreCase));
        if (mon.Handle == IntPtr.Zero)
            mon = monitors.FirstOrDefault(m => m.Primary);
        if (mon.Handle == IntPtr.Zero)
            mon = monitors[0];
        return string.IsNullOrWhiteSpace(mon.Device) ? [] : [mon.Device];
    }
}
