using System.Management;
using System.Runtime.Versioning;
using Lapassay.Core.Models;
using Microsoft.Win32;

namespace Lapassay.Core.Telemetry;

[SupportedOSPlatform("windows")]
public static class EnvironmentCapture
{
    public static EnvironmentInfo Capture()
    {
        return new EnvironmentInfo(
            Cpu: CaptureCpu(),
            Gpu: CaptureGpu(),
            Ram: CaptureRam(),
            Os: CaptureOs(),
            CapturedAt: DateTimeOffset.UtcNow);
    }

    static CpuInfo CaptureCpu()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L3CacheSize FROM Win32_Processor");
        foreach (var o in searcher.Get())
        {
            var name = (o["Name"] as string ?? "unknown").Trim();
            var physical = Convert.ToInt32(o["NumberOfCores"] ?? 0);
            var logical = Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0);
            // WMI's MaxClockSpeed is the RATED (base) clock, not max turbo — Windows has no
            // reliable turbo-clock query. Store it where it belongs; turbo stays 0 = unknown.
            // (It was previously stored in MaxTurboMhz, which mislabeled every report.)
            var baseMhz = Convert.ToInt32(o["MaxClockSpeed"] ?? 0);
            var l3Kb = Convert.ToInt32(o["L3CacheSize"] ?? 0);
            return new CpuInfo(name, physical, logical, baseMhz, 0, l3Kb / 1024);
        }
        return new CpuInfo("unknown", Environment.ProcessorCount, Environment.ProcessorCount, 0, 0, 0);
    }

    static List<GpuInfo> CaptureGpu()
    {
        var gpus = new List<GpuInfo>();
        // WMI's AdapterRAM is a 32-bit field — it caps at 4 GB, so every modern dGPU
        // (8 GB RTX 4070 etc.) was reported as ~4095 MB. DXGI reports the real value;
        // prefer it, keep the WMI number only as a fallback.
        var dxgiVram = DxgiVramByAdapterName();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
        foreach (var o in searcher.Get())
        {
            var name = (o["Name"] as string ?? "unknown").Trim();
            long ramBytes;
            try { ramBytes = Convert.ToInt64(o["AdapterRAM"] ?? 0); }
            catch { ramBytes = 0; }
            if (dxgiVram.TryGetValue(name, out var dxgiBytes) && dxgiBytes > 0)
                ramBytes = dxgiBytes;
            var driver = (o["DriverVersion"] as string ?? "unknown").Trim();
            gpus.Add(new GpuInfo(name, (int)(ramBytes / (1024 * 1024)), driver));
        }
        return gpus;
    }

    static Dictionary<string, long> DxgiVramByAdapterName()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Vortice.DXGI.DXGI.CreateDXGIFactory1(out Vortice.DXGI.IDXGIFactory1? factory);
            if (factory is null) return map;
            try
            {
                for (uint i = 0; ; i++)
                {
                    if (factory.EnumAdapters1(i, out Vortice.DXGI.IDXGIAdapter1? adapter).Failure || adapter is null)
                        break;
                    var desc = adapter.Description1;
                    adapter.Dispose();
                    var name = desc.Description.Trim();
                    var bytes = (long)desc.DedicatedVideoMemory;
                    if (!map.ContainsKey(name) || bytes > map[name]) map[name] = bytes;
                }
            }
            finally { factory.Dispose(); }
        }
        catch { /* DXGI unavailable (e.g. remote session edge cases) — WMI value stands */ }
        return map;
    }

    static RamInfo CaptureRam()
    {
        long totalBytes = 0;
        int speed = 0;
        int sticks = 0;
        using var searcher = new ManagementObjectSearcher(
            "SELECT Capacity, Speed FROM Win32_PhysicalMemory");
        foreach (var o in searcher.Get())
        {
            try { totalBytes += Convert.ToInt64(o["Capacity"] ?? 0); } catch { }
            try
            {
                var s = Convert.ToInt32(o["Speed"] ?? 0);
                if (s > 0 && speed == 0) speed = s;
            }
            catch { }
            sticks++;
        }
        // Channels: heuristic — 2+ sticks = dual-channel on typical laptops
        var channels = sticks >= 2 ? 2 : 1;
        return new RamInfo((int)(totalBytes / (1024L * 1024 * 1024)), speed, channels);
    }

    static OsInfo CaptureOs()
    {
        string build = "unknown";
        string bios = "unknown";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k != null)
            {
                var currentBuild = k.GetValue("CurrentBuildNumber") as string;
                var ubr = k.GetValue("UBR");
                build = currentBuild ?? "unknown";
                if (ubr != null) build = $"{build}.{ubr}";
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (var o in searcher.Get())
            {
                bios = (o["SMBIOSBIOSVersion"] as string ?? "unknown").Trim();
                break;
            }
        }
        catch { }

        var powerPlan = ReadActivePowerPlan();
        var onBattery = ReadOnBattery();

        return new OsInfo(build, bios, powerPlan, onBattery);
    }

    static string ReadActivePowerPlan()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2\power",
                "SELECT ElementName FROM Win32_PowerPlan WHERE IsActive = true");
            foreach (var o in searcher.Get())
            {
                return (o["ElementName"] as string ?? "unknown").Trim();
            }
        }
        catch { }
        return "unknown";
    }

    static bool ReadOnBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
            foreach (var o in searcher.Get())
            {
                // BatteryStatus: 1 = Discharging, 2 = AC. Statuses 4 (Low) and 5 (Critical)
                // also occur while discharging — count them as on-battery.
                var status = Convert.ToInt32(o["BatteryStatus"] ?? 0);
                return status is 1 or 4 or 5;
            }
        }
        catch { }
        return false;
    }
}
