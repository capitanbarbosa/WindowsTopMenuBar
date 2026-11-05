using System.IO;
using System.Threading;
using System.Windows;
using WindowsTopMenuBar.Native;
using WindowsTopMenuBar.Services;
using WindowsTopMenuBar.Views;

namespace WindowsTopMenuBar;

public partial class App : Application
{
    Mutex? _mutex;
    bool _ownsPanel;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Install(this);
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--probe-desktops", StringComparison.OrdinalIgnoreCase)))
        {
            Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
            string text = VirtualDesktopService.Instance.Describe();
            Console.WriteLine(text);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "WindowsTopMenuBar-probe.txt"), text);
            Shutdown();
            return;
        }

        bool created = false;
        try
        {
            _mutex = new Mutex(true, @"Local\WindowsTopMenuBar.Panel", out created);
        }
        catch (AbandonedMutexException)
        {
            created = true;
        }
        if (!created)
        {
            Log.Error("already running");
            MessageBox.Show(
                "Windows Top Menu Bar is already running.\n\nIf you do not see the bar, a crashed copy is still in memory. End WindowsTopMenuBar.exe in Task Manager, or run Stop Windows Top Menu Bar.cmd, then start it again.",
                "Windows Top Menu Bar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsPanel = true;
        SettingsStore.Instance.Load();
        StartupService.Apply(SettingsStore.Instance.Config.RunAtStartup);
        _ = AppCatalogService.Instance.LoadAsync().ContinueWith(
            t => { if (t.IsFaulted) Log.Error("catalog task", t.Exception); },
            TaskScheduler.Default);
        WindowTracker.Instance.ProcessAppeared += path => SettingsStore.Instance.RecordLaunch(path);
        WindowTracker.Instance.Start();

        SessionEnding += (_, _) => PanelManager.Instance.PrepareExit();
        PanelManager.Instance.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsPanel)
        {
            WindowTracker.Instance.Stop();
            SettingsStore.Instance.SaveNow();
        }
        try { _mutex?.ReleaseMutex(); } catch { /* already released */ }
        base.OnExit(e);
    }
}
