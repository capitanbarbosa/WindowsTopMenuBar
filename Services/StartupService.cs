using System.Diagnostics;
using Microsoft.Win32;
using WindowsWhisker.Models;
using WindowsWhisker.Native;

namespace WindowsWhisker.Services;

public static class StartupService
{
    const string ValueName = "WindowsWhisker";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                return;
            }
            string path = Environment.ProcessPath ?? "";
            if (path.Length == 0) return;
            key.SetValue(ValueName, "\"" + path + "\"");
        }
        catch (Exception ex)
        {
            Log.Error("startup", ex);
        }
    }
}

public static class SessionCommands
{
    public static void Execute(CommandItem cmd, Action openSettings)
    {
        switch (cmd.Kind)
        {
            case "settings":
                openSettings();
                return;
            case "lock":
                Win32.LockWorkStation();
                return;
            case "sleep":
                if (!Win32.SetSuspendState(false, false, false))
                    throw new InvalidOperationException("Windows declined to sleep.");
                return;
            case "logoff":
                Start("shutdown.exe", "/l");
                return;
            case "restart":
                Start("shutdown.exe", "/r /t 0");
                return;
            case "shutdown":
                Start("shutdown.exe", "/s /t 0");
                return;
            default:
                StartCommand(cmd.CommandLine);
                return;
        }
    }

    public static void StartCommand(string command)
    {
        var (file, args) = Split(command);
        if (string.IsNullOrWhiteSpace(file))
            throw new InvalidOperationException("The command is empty.");
        Start(file, args);
    }

    public static void ShowDesktop()
    {
        Win32.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        Win32.keybd_event(0x44, 0, 0, UIntPtr.Zero);
        Win32.keybd_event(0x44, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Win32.keybd_event(0x5B, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void OpenClipboard()
    {
        Win32.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        Win32.keybd_event(0x56, 0, 0, UIntPtr.Zero);
        Win32.keybd_event(0x56, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Win32.keybd_event(0x5B, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    static void Start(string file, string args) =>
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });

    static (string file, string args) Split(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return ("", "");
        if (command[0] == '"')
        {
            int end = command.IndexOf('"', 1);
            if (end > 1)
                return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        if (space < 0) return (command, "");
        return (command[..space], command[(space + 1)..].Trim());
    }
}
