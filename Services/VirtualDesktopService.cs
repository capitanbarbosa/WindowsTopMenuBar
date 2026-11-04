using System.Runtime.InteropServices;
using Microsoft.Win32;
using WindowsWhisker.Models;

namespace WindowsWhisker.Services;

/// <summary>
/// Virtual desktops on Windows 11 24H2 / 25H2 (builds 26100 and 26200).
/// The manager interface is undocumented and its layout matches that generation.
/// </summary>
public sealed class VirtualDesktopService
{
    public static VirtualDesktopService Instance { get; } = new();

    static readonly Guid AllDesktops1 = new("BB64D5B7-4DE3-4AB2-A87C-DB7601AEA7DC");
    static readonly Guid AllDesktops2 = new("C2DDEA68-66F2-4CF9-8264-1BFD00FBBBAC");
    static readonly Guid ClsidImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    static readonly Guid SidManager = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    static readonly Guid ClsidVirtualDesktopManager = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    IVirtualDesktopManagerInternal? _internal;
    IVirtualDesktopManagerPublic? _public;
    bool _numberedOnFirstRun;
    string? _error;
    bool _attempted;

    public bool IsAvailable => Ensure() && _internal != null;
    public string? Error => _error;

    public bool IsOnCurrentDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        try
        {
            EnsurePublic();
            if (_public == null) return true;
            return _public.IsWindowOnCurrentVirtualDesktop(hwnd);
        }
        catch
        {
            return true;
        }
    }

    public Guid GetWindowDesktopId(IntPtr hwnd)
    {
        try
        {
            EnsurePublic();
            if (_public == null) return Guid.Empty;
            return _public.GetWindowDesktopId(hwnd);
        }
        catch
        {
            return Guid.Empty;
        }
    }

    public void MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)
    {
        EnsurePublic();
        if (_public == null) return;
        _public.MoveWindowToDesktop(hwnd, ref desktopId);
    }

    public IReadOnlyList<DesktopInfo> GetDesktops()
    {
        if (!Ensure() || _internal == null) return [];
        var result = new List<DesktopInfo>();
        IObjectArray? array = null;
        try
        {
            _internal.GetDesktops(out array);
            array.GetCount(out int count);
            Guid current = Guid.Empty;
            IVirtualDesktop? currentDesktop = null;
            try
            {
                currentDesktop = _internal.GetCurrentDesktop();
                current = currentDesktop.GetId();
            }
            catch { /* current highlight is optional */ }
            finally
            {
                if (currentDesktop != null) Marshal.ReleaseComObject(currentDesktop);
            }

            Guid iid = typeof(IVirtualDesktop).GUID;
            for (int i = 0; i < count; i++)
            {
                array.GetAt(i, ref iid, out var obj);
                var desktop = (IVirtualDesktop)obj;
                Guid id = desktop.GetId();
                result.Add(new DesktopInfo
                {
                    Id = id,
                    Index = i,
                    Name = NameOf(desktop, id, i),
                    IsCurrent = id == current
                });
                Marshal.ReleaseComObject(desktop);
            }
        }
        finally
        {
            if (array != null) Marshal.ReleaseComObject(array);
        }
        return result;
    }

    public void SwitchTo(Guid id)
    {
        if (!Ensure() || _internal == null) return;
        var desktop = Find(id) ?? throw new InvalidOperationException("That desktop is no longer there.");
        try
        {
            try { _internal.SwitchDesktop(desktop); }
            catch { _internal.SwitchDesktopWithAnimation(desktop); }
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }
    }

    public void Create()
    {
        if (!Ensure() || _internal == null) return;
        var created = _internal.CreateDesktop();
        if (created != null) Marshal.ReleaseComObject(created);
    }

    public void Remove(Guid id)
    {
        if (!Ensure() || _internal == null) return;
        var all = GetRaw();
        if (all.Count <= 1) return;
        var target = all.FirstOrDefault(d => d.GetId() == id);
        if (target == null) return;
        var fallback = all.First(d => d.GetId() != id);
        _internal.RemoveDesktop(target, fallback);
        foreach (var d in all) Marshal.ReleaseComObject(d);
    }

    void NumberDesktopsOnFirstRun()
    {
        if (_numberedOnFirstRun || !SettingsStore.Instance.FirstRun) return;
        _numberedOnFirstRun = true;
        try
        {
            var desktops = GetDesktops();
            for (int i = 0; i < desktops.Count; i++)
            {
                string name = (i + 1).ToString();
                if (!string.Equals(desktops[i].Name, name, StringComparison.Ordinal))
                    Rename(desktops[i].Id, name);
            }
        }
        catch (Exception ex)
        {
            _numberedOnFirstRun = false;
            Log.Error("number desktops", ex);
        }
    }

    public void Rename(Guid id, string name)
    {
        if (!Ensure() || _internal == null) return;
        var desktop = Find(id);
        if (desktop == null) return;
        name ??= "";
        IntPtr hstring = IntPtr.Zero;
        try
        {
            int hr = WindowsCreateString(name, (uint)name.Length, out hstring);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            _internal.SetDesktopName(desktop, hstring);
            WriteName(id, name);
        }
        finally
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            Marshal.ReleaseComObject(desktop);
        }
    }

    public string Describe()
    {
        if (!Ensure() || _internal == null)
            return "unavailable: " + (_error ?? "unknown");
        var lines = new List<string>();
        foreach (var d in GetDesktops())
            lines.Add($"{d.Index + 1}\t{d.Id}\t{(d.IsCurrent ? "*" : " ")}\t{d.Name}");
        return string.Join(Environment.NewLine, lines);
    }

    bool Ensure()
    {
        if (_attempted) return _internal != null;
        _attempted = true;
        try
        {
            EnsurePublic();
            var shell = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidImmersiveShell)!)!;
            Guid sid = SidManager;
            Guid iid = typeof(IVirtualDesktopManagerInternal).GUID;
            _internal = (IVirtualDesktopManagerInternal)shell.QueryService(ref sid, ref iid);
            NumberDesktopsOnFirstRun();
            return true;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Log.Error("virtual desktop init", ex);
            return false;
        }
    }

    void EnsurePublic()
    {
        if (_public != null) return;
        try
        {
            _public = (IVirtualDesktopManagerPublic)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidVirtualDesktopManager)!)!;
        }
        catch (Exception ex)
        {
            Log.Error("public virtual desktop", ex);
        }
    }

    IVirtualDesktop? Find(Guid id)
    {
        if (_internal == null) return null;
        var copy = id;
        try { return _internal.FindDesktop(ref copy); }
        catch { return null; }
    }

    List<IVirtualDesktop> GetRaw()
    {
        var list = new List<IVirtualDesktop>();
        if (_internal == null) return list;
        _internal.GetDesktops(out var array);
        array.GetCount(out int count);
        Guid iid = typeof(IVirtualDesktop).GUID;
        for (int i = 0; i < count; i++)
        {
            array.GetAt(i, ref iid, out var obj);
            list.Add((IVirtualDesktop)obj);
        }
        Marshal.ReleaseComObject(array);
        return list;
    }

    static string NameOf(IVirtualDesktop desktop, Guid id, int index)
    {
        try
        {
            string live = FromHString(desktop.GetName());
            if (!string.IsNullOrWhiteSpace(live)) return live.Trim();
        }
        catch { /* older IVirtualDesktop layouts omit GetName */ }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NameKey(id));
            if (key?.GetValue("Name") is string name && !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch { /* registry layout varies */ }
        return "Desktop " + (index + 1);
    }

    static void WriteName(Guid id, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(NameKey(id));
            if (key == null) return;
            if (string.IsNullOrEmpty(name)) key.DeleteValue("Name", false);
            else key.SetValue("Name", name, RegistryValueKind.String);
        }
        catch { /* the shell copy of the name is enough */ }
    }

    static string NameKey(Guid id) =>
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{" + id.ToString().ToUpperInvariant() + "}";

    static string FromHString(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero) return "";
        try
        {
            IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
            return buffer == IntPtr.Zero ? "" : (Marshal.PtrToStringUni(buffer, (int)length) ?? "");
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    public static bool IsAllDesktops(Guid id) => id == AllDesktops1 || id == AllDesktops2;
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
interface IServiceProvider10
{
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object QueryService(ref Guid service, ref Guid riid);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
interface IObjectArray
{
    void GetCount(out int count);
    void GetAt(int index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
interface IVirtualDesktop
{
    bool IsViewVisible(IntPtr view);
    Guid GetId();
    IntPtr GetName();
    IntPtr GetWallpaperPath();
    bool IsRemote();
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("53F5CA0B-158F-4124-900C-057158060B27")]
interface IVirtualDesktopManagerInternal
{
    int GetCount();
    void MoveViewToDesktop(IntPtr view, IVirtualDesktop desktop);
    bool CanViewMoveDesktops(IntPtr view);
    IVirtualDesktop GetCurrentDesktop();
    void GetDesktops(out IObjectArray desktops);
    [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop from, int direction, out IVirtualDesktop desktop);
    void SwitchDesktop(IVirtualDesktop desktop);
    void SwitchDesktopAndMoveForegroundView(IVirtualDesktop desktop);
    IVirtualDesktop CreateDesktop();
    void MoveDesktop(IVirtualDesktop desktop, int nIndex);
    void RemoveDesktop(IVirtualDesktop desktop, IVirtualDesktop fallback);
    IVirtualDesktop FindDesktop(ref Guid desktopId);
    void GetDesktopSwitchIncludeExcludeViews(IVirtualDesktop desktop, out IObjectArray unknown1, out IObjectArray unknown2);
    void SetDesktopName(IVirtualDesktop desktop, IntPtr name);
    void SetDesktopWallpaper(IVirtualDesktop desktop, IntPtr path);
    void UpdateWallpaperPathForAllDesktops(IntPtr path);
    void CopyDesktopState(IntPtr pView0, IntPtr pView1);
    void CreateRemoteDesktop(IntPtr path, out IVirtualDesktop desktop);
    void SwitchRemoteDesktop(IVirtualDesktop desktop, IntPtr switchType);
    void SwitchDesktopWithAnimation(IVirtualDesktop desktop);
    void GetLastActiveDesktop(out IVirtualDesktop desktop);
    void WaitForAnimationToComplete();
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
interface IVirtualDesktopManagerPublic
{
    bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
    Guid GetWindowDesktopId(IntPtr topLevelWindow);
    void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}
