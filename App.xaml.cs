using System.IO;
using System.Threading;
using System.Windows;
using WindowsWhisker.Native;
using WindowsWhisker.Services;
using WindowsWhisker.Views;

namespace WindowsWhisker;

public partial class App : Application
{
    Mutex? _mutex;
    int _errors;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("ui", args.Exception);
            if (_errors++ < 2)
                MessageBox.Show(args.Exception.Message, "Windows Whisker");
            args.Handled = true;
        };

        if (e.Args.Any(a => a.Equals("--probe-desktops", StringComparison.OrdinalIgnoreCase)))
        {
            Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
            string text = VirtualDesktopService.Instance.Describe();
            Console.WriteLine(text);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "whisker-probe.txt"), text);
            Shutdown();
            return;
        }

        bool created = false;
        try
        {
            _mutex = new Mutex(true, @"Local\WindowsWhisker.Panel", out created);
        }
        catch (AbandonedMutexException)
        {
            created = true;
        }
        if (!created)
        {
            Shutdown();
            return;
        }

        SettingsStore.Instance.Load();
        StartupService.Apply(SettingsStore.Instance.Config.RunAtStartup);
        _ = AppCatalogService.Instance.LoadAsync().ContinueWith(
            t => { if (t.IsFaulted) Log.Error("catalog task", t.Exception); },
            TaskScheduler.Default);
        WindowTracker.Instance.Start();

        var panel = new PanelWindow();
        MainWindow = panel;
        SessionEnding += (_, _) => panel.AllowClose();
        panel.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WindowTracker.Instance.Stop();
        SettingsStore.Instance.SaveNow();
        try { _mutex?.ReleaseMutex(); } catch { /* already released */ }
        base.OnExit(e);
    }
}
