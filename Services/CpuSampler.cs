using WindowsTopMenuBar.Native;

namespace WindowsTopMenuBar.Services;

public static class CpuSampler
{
    static ulong _idle;
    static ulong _kernel;
    static ulong _user;
    static bool _primed;

    public static double Read()
    {
        if (!Win32.GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        ulong i = idle.Value;
        ulong k = kernel.Value;
        ulong u = user.Value;
        if (!_primed)
        {
            _idle = i;
            _kernel = k;
            _user = u;
            _primed = true;
            return 0;
        }

        ulong idleDelta = i - _idle;
        ulong kernelDelta = k - _kernel;
        ulong userDelta = u - _user;
        _idle = i;
        _kernel = k;
        _user = u;

        ulong total = kernelDelta + userDelta;
        if (total == 0) return 0;
        ulong busy = total - idleDelta;
        return Math.Clamp(100.0 * busy / total, 0, 100);
    }
}
