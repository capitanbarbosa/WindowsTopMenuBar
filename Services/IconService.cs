using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsWhisker.Models;
using WindowsWhisker.Native;

namespace WindowsWhisker.Services;

public static class IconService
{
    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(AppEntry app, int size)
    {
        if (!string.IsNullOrWhiteSpace(app.IconPath))
        {
            var fromIcon = Get(app.IconPath, app.IconIndex, size);
            if (fromIcon != null) return fromIcon;
        }
        if (!string.IsNullOrWhiteSpace(app.TargetPath) && File.Exists(Expand(app.TargetPath)))
        {
            var fromTarget = Get(app.TargetPath, 0, size);
            if (fromTarget != null) return fromTarget;
        }
        if (!string.IsNullOrWhiteSpace(app.Key) && File.Exists(app.Key))
            return Get(app.Key, 0, size);
        if (!string.IsNullOrWhiteSpace(app.TargetPath))
            return Get(app.TargetPath, 0, size);
        return null;
    }

    public static ImageSource? Get(string? path, int index, int size)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Expand(path);
        size = Math.Clamp(size, 16, 256);
        var key = path + "|" + index + "|" + size;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        ImageSource? image = null;
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase) ||
                ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")
                image = LoadBitmap(path, size);
            else
                image = Extract(path, index, size);
        }
        catch (Exception ex)
        {
            Log.Error("icon " + path, ex);
        }

        if (image != null)
        {
            if (Cache.Count > 600) Cache.Clear();
            Cache[key] = image;
        }
        return image;
    }

    public static ImageSource? FromHIconShared(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        IntPtr copy = IntPtr.Zero;
        try
        {
            copy = Win32.CopyIcon(hIcon);
            if (copy == IntPtr.Zero) return null;
            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                copy, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (copy != IntPtr.Zero) Win32.DestroyIcon(copy);
        }
    }

    static string Expand(string path)
    {
        try { return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')); }
        catch { return path; }
    }

    static ImageSource? LoadBitmap(string path, int size)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelWidth = size;
        img.EndInit();
        img.Freeze();
        return img;
    }

    static ImageSource? Extract(string path, int index, int size)
    {
        IntPtr large = IntPtr.Zero;
        IntPtr small = IntPtr.Zero;
        IntPtr shellIcon = IntPtr.Zero;
        try
        {
            uint sizePack = (uint)(size & 0xFFFF) | ((uint)Math.Max(16, size / 2) << 16);
            int hr = Win32.SHDefExtractIcon(path, index, 0, out large, out small, sizePack);
            IntPtr chosen = large != IntPtr.Zero ? large : small;
            if (hr == 0 && chosen != IntPtr.Zero)
                return CopyIcon(chosen);

            var info = new SHFILEINFO();
            uint flags = Win32.SHGFI_ICON | (size <= 20 ? Win32.SHGFI_SMALLICON : Win32.SHGFI_LARGEICON);
            IntPtr result = Win32.SHGetFileInfo(path, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            shellIcon = info.hIcon;
            return CopyIcon(shellIcon);
        }
        finally
        {
            if (large != IntPtr.Zero) Win32.DestroyIcon(large);
            if (small != IntPtr.Zero) Win32.DestroyIcon(small);
            if (shellIcon != IntPtr.Zero) Win32.DestroyIcon(shellIcon);
        }
    }

    static ImageSource? CopyIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bmp.Freeze();
        return bmp;
    }
}
