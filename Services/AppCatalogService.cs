using System.Runtime.InteropServices;
using System.Text;
using WindowsTopMenuBar.Models;
using WindowsTopMenuBar.Native;

namespace WindowsTopMenuBar.Services;

public sealed class AppCatalogService
{
    public static AppCatalogService Instance { get; } = new();

    readonly object _gate = new();
    List<AppEntry> _apps = [];
    public bool IsReady { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<AppEntry> Snapshot()
    {
        lock (_gate) return _apps.ToList();
    }

    public AppEntry? Find(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        lock (_gate)
            return _apps.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public AppEntry? FindByTarget(string? path, string? aumid = null)
    {
        var apps = Snapshot();
        if (!string.IsNullOrWhiteSpace(aumid))
        {
            var byAumid = apps.FirstOrDefault(a =>
                a.AppUserModelId.Equals(aumid, StringComparison.OrdinalIgnoreCase));
            if (byAumid != null) return byAumid;
        }
        if (string.IsNullOrWhiteSpace(path)) return null;
        var byKey = apps.FirstOrDefault(a => a.Key.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (byKey != null) return byKey;

        string full = NormalizePath(path);
        var matches = apps.Where(a => PathsEqual(a.TargetPath, full)).ToList();
        if (matches.Count == 0)
        {
            string name = Path.GetFileName(full);
            if (!string.IsNullOrWhiteSpace(name))
                matches = apps.Where(a => Path.GetFileName(a.TargetPath).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0];
        string stem = Path.GetFileNameWithoutExtension(full);
        return matches.FirstOrDefault(a => a.Name.Equals(stem, StringComparison.OrdinalIgnoreCase))
            ?? matches.OrderBy(a => a.Category == "Applications" ? 0 : 1).ThenBy(a => a.Name.Length).First();
    }

    static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        return NormalizePath(left).Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizePath(string path)
    {
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    public Task LoadAsync()
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                var apps = Scan();
                lock (_gate) _apps = apps;
                IsReady = true;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                    dispatcher.BeginInvoke(() => Changed?.Invoke());
                else
                    Changed?.Invoke();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                Log.Error("catalog", ex);
                IsReady = true;
                tcs.SetException(ex);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Name = "App catalog";
        thread.Start();
        return tcs.Task;
    }

    static List<AppEntry> Scan()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        var list = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var programs = Path.Combine(root, "Programs");
            if (!Directory.Exists(programs)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var file in files)
            {
                if (!seen.Add(file)) continue;
                var relDir = Path.GetDirectoryName(Path.GetRelativePath(programs, file)) ?? "";
                if (relDir.StartsWith("Startup", StringComparison.OrdinalIgnoreCase)) continue;
                var entry = ReadShortcut(file, relDir);
                if (entry != null) list.Add(entry);
            }
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    static AppEntry? ReadShortcut(string file, string relativeDir)
    {
        ShellLink? link = null;
        try
        {
            link = new ShellLink();
            var persist = (IPersistFile)link;
            persist.Load(file, 0);
            var shell = (IShellLinkW)link;

            var target = new StringBuilder(1024);
            shell.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var args = new StringBuilder(1024);
            shell.GetArguments(args, args.Capacity);

            var work = new StringBuilder(1024);
            shell.GetWorkingDirectory(work, work.Capacity);

            var desc = new StringBuilder(1024);
            shell.GetDescription(desc, desc.Capacity);

            var icon = new StringBuilder(1024);
            shell.GetIconLocation(icon, icon.Capacity, out int iconIndex);

            string aumid = ReadAumid(link);
            string targetPath = target.ToString().Trim();
            if (string.IsNullOrWhiteSpace(targetPath) && string.IsNullOrWhiteSpace(aumid))
                return null;

            string name = Path.GetFileNameWithoutExtension(file);
            string generic = string.IsNullOrWhiteSpace(targetPath)
                ? name
                : Path.GetFileNameWithoutExtension(targetPath);
            string category = "Applications";
            if (!string.IsNullOrWhiteSpace(relativeDir))
                category = relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

            return new AppEntry
            {
                Key = file,
                Name = name,
                GenericName = generic,
                Description = desc.ToString().Trim(),
                Category = category,
                TargetPath = targetPath,
                Arguments = args.ToString(),
                WorkingDirectory = work.ToString(),
                IconPath = string.IsNullOrWhiteSpace(icon.ToString()) ? targetPath : icon.ToString(),
                IconIndex = iconIndex,
                AppUserModelId = aumid
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link != null)
            {
                try { Marshal.ReleaseComObject(link); } catch { /* already released */ }
            }
        }
    }

    static string ReadAumid(object link)
    {
        try
        {
            var store = (IPropertyStore)link;
            var key = new PropertyKey
            {
                fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                pid = 5
            };
            store.GetValue(ref key, out var pv);
            try
            {
                if (pv.p == IntPtr.Zero) return "";
                if (pv.vt is 31 or 8)
                    return Marshal.PtrToStringUni(pv.p) ?? "";
                return "";
            }
            finally
            {
                Win32.PropVariantClear(ref pv);
            }
        }
        catch
        {
            return "";
        }
    }

    public static void Launch(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.AppUserModelId) &&
            (string.IsNullOrWhiteSpace(app.TargetPath) || !File.Exists(app.TargetPath)))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + app.AppUserModelId,
                UseShellExecute = true
            });
            return;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(app.TargetPath) ? app.Key : app.TargetPath,
            Arguments = app.Arguments ?? "",
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory) && Directory.Exists(app.WorkingDirectory))
            psi.WorkingDirectory = app.WorkingDirectory;
        System.Diagnostics.Process.Start(psi);
    }

    public static void LaunchTarget(string target, string arguments, string workingDirectory)
    {
        if (target.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + target["aumid:".Length..],
                UseShellExecute = true
            });
            return;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = target,
            Arguments = arguments ?? "",
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        System.Diagnostics.Process.Start(psi);
    }
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
class ShellLink
{
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
interface IPropertyStore
{
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PropertyKey pkey);
    void GetValue(ref PropertyKey key, out PropVariant pv);
}
