using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WindowsWhisker.Services;

public static class Log
{
    static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsWhisker");

    public static string FilePath { get; } = Path.Combine(Folder, "log.txt");
    public static string CrashPath { get; } = Path.Combine(Folder, "crash.txt");

    static int _installed;

    public static void Install(Application app)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Crash("process", args.ExceptionObject as Exception
                ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "unknown"));
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Crash("task", args.Exception);
            args.SetObserved();
        };
        app.DispatcherUnhandledException += (_, args) =>
        {
            Crash("ui", args.Exception);
            args.Handled = true;
        };
        app.Dispatcher.UnhandledExceptionFilter += (_, args) =>
        {
            Crash("ui-filter", args.Exception);
            args.RequestCatch = true;
        };

        Write(FilePath, $"---- start {Stamp()} pid={Environment.ProcessId} {Version()} {Environment.OSVersion} ----");
    }

    public static void Error(string message, Exception? ex = null)
    {
        var line = new StringBuilder();
        line.Append(Stamp()).Append(' ').Append(message);
        if (ex != null)
        {
            line.Append(' ');
            line.Append(ex);
        }
        Write(FilePath, line.ToString());
    }

    public static void Crash(string source, Exception? ex)
    {
        var dump = new StringBuilder();
        dump.AppendLine("==== Windows Whisker crash ====");
        dump.AppendLine("time:      " + Stamp());
        dump.AppendLine("source:    " + source);
        dump.AppendLine("version:   " + Version());
        dump.AppendLine("os:        " + Environment.OSVersion);
        dump.AppendLine("framework: " + RuntimeInformation.FrameworkDescription);
        dump.AppendLine("64-bit:    " + Environment.Is64BitProcess);
        dump.AppendLine("pid:       " + Environment.ProcessId);
        dump.AppendLine("thread:    " + Environment.CurrentManagedThreadId + " " + Thread.CurrentThread.Name);
        dump.AppendLine();
        WriteException(dump, ex);
        dump.AppendLine();

        string text = dump.ToString();
        Write(CrashPath, text);
        Write(FilePath, Stamp() + " crash " + source + " " + (ex?.GetType().FullName ?? "") + " " + (ex?.Message ?? ""));
    }

    public static void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(CrashPath))
                File.WriteAllText(CrashPath, "No crash has been written yet.\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = CrashPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Error("open crash log", ex);
        }
    }

    static void WriteException(StringBuilder dump, Exception? ex)
    {
        if (ex == null)
        {
            dump.AppendLine("(no exception object)");
            return;
        }
        for (var current = ex; current != null; current = current.InnerException)
        {
            dump.AppendLine(current.GetType().FullName);
            dump.AppendLine(current.Message);
            dump.AppendLine(current.StackTrace);
            dump.AppendLine();
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    WriteException(dump, inner);
            }
        }
    }

    static void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine(text);
            writer.Flush();
            stream.Flush(true);
        }
        catch
        {
            // Logging must not take the panel down.
        }
    }

    static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    static string Version() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
}
