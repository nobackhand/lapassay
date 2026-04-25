using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace Lapassay.Core;

/// <summary>
/// Enforces privileges required for accurate, reproducible benchmarking:
///   - Admin: needed for RAPL MSR reads (CPU package power via LibreHardwareMonitor driver).
///   - Developer Mode: needed for ID3D12Device::SetStablePowerState() to lock GPU clocks.
/// Callers should invoke Check() early; on failure it returns actionable messages.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Preflight
{
    public record Result(bool IsAdmin, bool DeveloperMode, IReadOnlyList<string> Messages)
    {
        public bool Ok => IsAdmin && DeveloperMode;
    }

    public static Result Check()
    {
        var isAdmin = IsAdmin();
        var devMode = IsDeveloperModeEnabled();
        var msgs = new List<string>();
        if (!isAdmin)
        {
            msgs.Add("Not running as administrator. CPU package power (RAPL) telemetry will be unavailable.");
            msgs.Add("  Fix: re-run from an elevated terminal (right-click \"Run as administrator\").");
        }
        if (!devMode)
        {
            msgs.Add("Windows Developer Mode is not enabled. GPU clocks cannot be locked, so GPU scores will have 10-30% run-to-run variance.");
            msgs.Add("  Fix: Settings > System > For developers > enable \"Developer Mode\".");
        }
        return new Result(isAdmin, devMode, msgs);
    }

    static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static bool IsDeveloperModeEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            if (key?.GetValue("AllowDevelopmentWithoutDevLicense") is int v && v == 1)
                return true;
        }
        catch { }
        return false;
    }
}
