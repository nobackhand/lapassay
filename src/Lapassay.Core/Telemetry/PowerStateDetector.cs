using System.Management;
using System.Runtime.Versioning;

namespace Lapassay.Core.Telemetry;

public enum PowerState
{
    Unknown = 0,
    OnBattery,
    OnAc,
}

[SupportedOSPlatform("windows")]
public static class PowerStateDetector
{
    /// <summary>Reads the current AC vs battery state via WMI. Falls back to Unknown
    /// on systems with no battery (desktops) or transient WMI errors.</summary>
    public static PowerState Current()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
            foreach (var o in searcher.Get())
            {
                // BatteryStatus codes (Win32_Battery): 1 = Discharging, 2 = AC, 3 = Fully charged on AC,
                // 4 = Low, 5 = Critical, 6 = Charging, 7 = Charging+High, 8 = Charging+Low, 9 = Charging+Critical,
                // 10 = Undefined, 11 = Partially charged.
                var status = Convert.ToInt32(o["BatteryStatus"] ?? 0);
                return status == 1 ? PowerState.OnBattery : PowerState.OnAc;
            }
        }
        catch { /* swallow */ }
        return PowerState.Unknown;
    }

    /// <summary>Polls the WMI battery state until it flips to <paramref name="target"/> or
    /// the cancellation token fires. Returns the actual final state.</summary>
    public static async Task<PowerState> WaitForAsync(PowerState target, CancellationToken ct, int pollIntervalMs = 750)
    {
        while (!ct.IsCancellationRequested)
        {
            var s = Current();
            if (s == target) return s;
            try { await Task.Delay(pollIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
        return Current();
    }

    public static string Describe(PowerState s) => s switch
    {
        PowerState.OnAc => "AC",
        PowerState.OnBattery => "Battery",
        _ => "Unknown",
    };
}
