using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapassay.Core.Harness;

[SupportedOSPlatform("windows")]
public static class ThreadPinning
{
    [DllImport("kernel32.dll")]
    static extern IntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentThread();

    /// <summary>Pin the current thread to a specific logical processor (0-based).</summary>
    public static void PinCurrentThread(int logicalProcessor)
    {
        var mask = (UIntPtr)(1UL << logicalProcessor);
        SetThreadAffinityMask(GetCurrentThread(), mask);
    }
}
