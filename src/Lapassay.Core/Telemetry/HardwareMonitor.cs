using System.Management;
using System.Runtime.Versioning;
using Lapassay.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace Lapassay.Core.Telemetry;

public record TelemetrySample(
    DateTimeOffset Timestamp,
    double? CpuPkgWatts,
    double? GpuWatts,
    double? CpuTempC,
    double? GpuTempC,
    int? CpuMhz);

/// <summary>
/// Wraps LibreHardwareMonitor.Hardware.Computer for per-run telemetry sampling.
/// Start() begins a 100ms sampler in a background task; Stop() returns buffered samples.
/// Requires admin for RAPL MSR reads (CPU package power).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareMonitor : IDisposable
{
    readonly Computer _computer;
    readonly UpdateVisitor _visitor = new();
    readonly List<TelemetrySample> _samples = new();
    CancellationTokenSource? _cts;
    Task? _loop;
    readonly int _intervalMs;

    public HardwareMonitor(int intervalMs = 100)
    {
        _intervalMs = intervalMs;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = false,
            IsMemoryEnabled = false,
            IsStorageEnabled = false,
            IsBatteryEnabled = true,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
        };
        _computer.Open();
    }

    /// <summary>Optional callback fired on each new sample. Invoked on the sampler's background thread.</summary>
    public Action<TelemetrySample>? OnSample { get; set; }

    public void Start()
    {
        _samples.Clear();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                TelemetrySample? sample = null;
                try
                {
                    _computer.Accept(_visitor);
                    sample = Snapshot();
                    _samples.Add(sample);
                }
                catch { /* ignore transient sensor glitches */ }
                if (sample is not null)
                {
                    try { OnSample?.Invoke(sample); } catch { /* never let consumers kill the loop */ }
                }
                try { await Task.Delay(_intervalMs, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    public IReadOnlyList<TelemetrySample> Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1000); } catch { }
        return _samples.ToArray();
    }

    /// <summary>Most recent sample (or null if none yet). Safe to call while the sampler is running.</summary>
    public TelemetrySample? Latest()
    {
        // List<T>.Count read is volatile-enough for our purpose; we tolerate occasional misses.
        var n = _samples.Count;
        return n == 0 ? null : _samples[n - 1];
    }

    TelemetrySample Snapshot()
    {
        double? cpuPkgW = null, gpuW = null, cpuTemp = null, gpuTemp = null;
        int? cpuMhz = null;

        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType == HardwareType.Cpu)
            {
                foreach (var s in hw.Sensors)
                {
                    // Treat 0 as "no real reading" — AMD chips with non-standard SMUs report
                    // sensor names but stream literal zeros, which we shouldn't display as "0 W"
                    // or "0 °C".
                    if (s.SensorType == SensorType.Power && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) && s.Value is float pw && pw > 0)
                        cpuPkgW ??= pw;
                    else if (s.SensorType == SensorType.Temperature && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) && s.Value is float pt && pt > 0)
                        cpuTemp ??= pt;
                    else if (s.SensorType == SensorType.Clock && s.Value is float cv && cv > 0 && cpuMhz is null && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                        cpuMhz = (int)Math.Round(cv);
                }
                // Fallback: max core temp if no package temp.
                if (cpuTemp is null)
                {
                    var coreTemps = hw.Sensors
                        .Where(s => s.SensorType == SensorType.Temperature && s.Value is float v && v > 0)
                        .Select(s => (double)s.Value!.Value)
                        .ToArray();
                    if (coreTemps.Length > 0) cpuTemp = coreTemps.Max();
                }
            }
            else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType == SensorType.Power && s.Value is float pw && pw > 0)
                        gpuW ??= pw;
                    else if (s.SensorType == SensorType.Temperature && s.Value is float pt && pt > 0)
                        gpuTemp ??= pt;
                }
            }
        }

        // ACPI thermal-zone fallback: many laptops expose CPU temp via WMI's
        // MSAcpi_ThermalZoneTemperature even when LHM's MSR/SMU path returns zeros.
        // Refresh at most once per second; the WMI query takes ~10 ms.
        if (cpuTemp is null)
            cpuTemp = ReadAcpiCpuTempCached();

        return new TelemetrySample(DateTimeOffset.UtcNow, cpuPkgW, gpuW, cpuTemp, gpuTemp, cpuMhz);
    }

    DateTimeOffset _acpiCacheAt;
    double? _acpiCachedCpuTemp;

    double? ReadAcpiCpuTempCached()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _acpiCacheAt).TotalMilliseconds < 900) return _acpiCachedCpuTemp;
        _acpiCacheAt = now;
        _acpiCachedCpuTemp = ReadAcpiCpuTemp();
        return _acpiCachedCpuTemp;
    }

    static double? ReadAcpiCpuTemp()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double? cpuZone = null;
            double? bestOther = null;
            foreach (var o in searcher.Get())
            {
                int k10;
                try { k10 = Convert.ToInt32(o["CurrentTemperature"] ?? 0); }
                catch { continue; }
                if (k10 == 0) continue;
                var c = k10 / 10.0 - 273.15;
                var name = (o["InstanceName"] as string ?? "").ToUpperInvariant();
                // Most laptops name the CPU zone "CPUZ" (or have it under "CPU" in some BIOS).
                if (name.Contains("CPUZ") || name.Contains("CPU0") || name.Contains("CPU_"))
                {
                    cpuZone = c;
                }
                // Skip irrelevant zones for the fallback max.
                else if (!name.Contains("BATZ") && !name.Contains("CHGZ") && !name.Contains("BAGZ") && !name.Contains("GFXZ"))
                {
                    if (bestOther is null || c > bestOther) bestOther = c;
                }
            }
            return cpuZone ?? bestOther;
        }
        catch
        {
            return null;
        }
    }

    public static TelemetrySummary Summarize(IReadOnlyList<TelemetrySample> samples)
    {
        if (samples.Count == 0)
            return new TelemetrySummary(null, null, null, null, null, null, null, null);

        double? avg(IEnumerable<double?> xs)
        {
            var vals = xs.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return vals.Length == 0 ? null : vals.Average();
        }
        double? max(IEnumerable<double?> xs)
        {
            var vals = xs.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return vals.Length == 0 ? null : vals.Max();
        }
        int? imax(IEnumerable<int?> xs)
        {
            var vals = xs.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return vals.Length == 0 ? null : vals.Max();
        }
        int? imin(IEnumerable<int?> xs)
        {
            var vals = xs.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return vals.Length == 0 ? null : vals.Min();
        }

        return new TelemetrySummary(
            CpuPkgWattsAvg: avg(samples.Select(s => s.CpuPkgWatts)),
            CpuPkgWattsMax: max(samples.Select(s => s.CpuPkgWatts)),
            GpuWattsAvg: avg(samples.Select(s => s.GpuWatts)),
            GpuWattsMax: max(samples.Select(s => s.GpuWatts)),
            CpuTempCMax: max(samples.Select(s => s.CpuTempC)),
            GpuTempCMax: max(samples.Select(s => s.GpuTempC)),
            CpuMhzMin: imin(samples.Select(s => s.CpuMhz)),
            CpuMhzMax: imax(samples.Select(s => s.CpuMhz)));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _computer.Close(); } catch { }
    }

    sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
