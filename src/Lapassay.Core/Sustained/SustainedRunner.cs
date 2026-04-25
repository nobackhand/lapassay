using System.Diagnostics;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;
using Lapassay.Core.Kernels.Cpu;
using Lapassay.Core.Kernels.Gpu;
using Lapassay.Core.Models;
using Lapassay.Core.Telemetry;

namespace Lapassay.Core.Sustained;

/// <summary>
/// Sustained throttle test: tightly loops one CPU SGEMM and one GPU matmul iteration
/// for N seconds, sampling throughput + telemetry per cycle. Compares first-window vs
/// last-window medians to detect thermal throttling that single-shot benchmarks miss.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SustainedRunner
{
    public record RunOptions(double DurationSec, int CpuN = 1024, int GpuN = 2048);

    public static SustainedRun Run(
        RunOptions options,
        Action<SustainedSample>? onSample = null,
        Action<string>? log = null,
        CancellationToken cancel = default)
    {
        log ??= _ => { };
        log($"Sustained run, target duration {options.DurationSec:F0}s");
        log("Capturing environment...");
        var env = EnvironmentCapture.Capture();

        ThreadPinning.PinCurrentThread(0);

        // Setup CPU kernel (one-time init).
        var sgemm = new SgemmKernel(options.CpuN);

        // Setup GPU kernel (one-time init).
        var preflight = Preflight.Check();
        using var ctx = new D3D12Context(enableStablePowerState: preflight.DeveloperMode);
        if (!ctx.StablePowerStateEnabled)
            log("  (Dev Mode off: GPU clocks unlocked — wider variance baseline)");
        log($"  GPU adapter: {ctx.AdapterName}");

        // measurementIterations=1 → single timed dispatch per cycle.
        using var matmul = new Fp32MatmulKernel(ctx, options.GpuN, measurementIterations: 1);

        // Warmup: 5 of each, throw away.
        log("Warming up...");
        for (var i = 0; i < 5; i++) sgemm.Run();
        matmul.Measure(warmupIterations: 5);

        using var monitor = new HardwareMonitor(intervalMs: 200);
        monitor.Start();

        var samples = new List<SustainedSample>();
        var sw = Stopwatch.StartNew();
        var lastLog = -10.0;
        var iter = 0;

        while (sw.Elapsed.TotalSeconds < options.DurationSec)
        {
            if (cancel.IsCancellationRequested) break;

            // CPU iteration
            var cpuSw = Stopwatch.StartNew();
            sgemm.Run();
            cpuSw.Stop();
            var cpuGflops = sgemm.Gflops(cpuSw.Elapsed.TotalSeconds);

            // GPU iteration (single timed dispatch, no warmup)
            var gpuTimes = matmul.Measure(warmupIterations: 0);
            var gpuTime = gpuTimes.Length > 0 ? gpuTimes[0] : 0;
            var gpuGflops = gpuTime > 0 ? matmul.Gflops(gpuTime) : 0;

            var elapsed = sw.Elapsed.TotalSeconds;

            // Pull most recent telemetry sample (best-effort; could be slightly stale).
            var t = LatestTelemetry(monitor);

            var sample = new SustainedSample(
                ElapsedSec: elapsed,
                CpuGflops: cpuGflops,
                GpuGflops: gpuGflops,
                CpuPkgWatts: t.cpuW,
                GpuWatts: t.gpuW,
                CpuTempC: t.cpuT,
                GpuTempC: t.gpuT,
                CpuMhz: t.cpuMhz);

            samples.Add(sample);
            onSample?.Invoke(sample);

            if (elapsed - lastLog >= 10)
            {
                log($"  t={elapsed:F0}s  CPU {cpuGflops:F1} GFLOPS  GPU {gpuGflops:F1} GFLOPS" +
                    (t.cpuT.HasValue ? $"  cpuT {t.cpuT:F0}°C" : "") +
                    (t.gpuT.HasValue ? $"  gpuT {t.gpuT:F0}°C" : ""));
                lastLog = elapsed;
            }

            iter++;
        }

        monitor.Stop();
        sw.Stop();
        log($"Done. {iter} iterations in {sw.Elapsed.TotalSeconds:F1}s.");

        var verdict = ComputeVerdict(samples, totalDurationSec: sw.Elapsed.TotalSeconds);
        if (verdict.Throttled)
            log($"⚠ THROTTLE DETECTED: CPU dropped {verdict.CpuDropPct:F1}%, GPU dropped {verdict.GpuDropPct:F1}%");
        else
            log($"✓ No significant throttling (CPU drop {verdict.CpuDropPct:F1}%, GPU drop {verdict.GpuDropPct:F1}%)");

        var runId = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}-{Environment.MachineName.ToLowerInvariant()}-sustained-{Guid.NewGuid().ToString()[..8]}";
        return new SustainedRun(
            SchemaVersion: "1.0",
            Tool: "lapassay",
            ToolVersion: "0.4.0",
            RunId: runId,
            Environment: env,
            DurationSec: sw.Elapsed.TotalSeconds,
            IterationCount: iter,
            Verdict: verdict,
            Samples: samples);
    }

    /// <summary>First-30s vs last-60s window-median comparison. Throttle flagged if drop &gt; 5% on either CPU or GPU.</summary>
    public static ThrottleVerdict ComputeVerdict(IReadOnlyList<SustainedSample> samples, double totalDurationSec)
    {
        const double FirstWindowEnd = 30.0;
        const double LastWindowSec = 60.0;
        const double ThrottleThreshold = 0.05;

        var first = samples.Where(s => s.ElapsedSec >= 0 && s.ElapsedSec < FirstWindowEnd).ToList();
        var lastWindowStart = Math.Max(FirstWindowEnd, totalDurationSec - LastWindowSec);
        var last = samples.Where(s => s.ElapsedSec >= lastWindowStart).ToList();

        if (first.Count == 0 || last.Count == 0)
            return new ThrottleVerdict(false, 0, 0, 0, 0, 0, 0);

        var firstCpu = Median(first.Select(s => s.CpuGflops));
        var lastCpu  = Median(last.Select(s => s.CpuGflops));
        var firstGpu = Median(first.Select(s => s.GpuGflops));
        var lastGpu  = Median(last.Select(s => s.GpuGflops));

        var cpuDrop = firstCpu > 0 ? (firstCpu - lastCpu) / firstCpu : 0;
        var gpuDrop = firstGpu > 0 ? (firstGpu - lastGpu) / firstGpu : 0;
        var throttled = cpuDrop > ThrottleThreshold || gpuDrop > ThrottleThreshold;

        return new ThrottleVerdict(
            Throttled: throttled,
            CpuDropPct: cpuDrop * 100,
            GpuDropPct: gpuDrop * 100,
            FirstWindowCpuGflops: firstCpu,
            LastWindowCpuGflops: lastCpu,
            FirstWindowGpuGflops: firstGpu,
            LastWindowGpuGflops: lastGpu);
    }

    static double Median(IEnumerable<double> xs)
    {
        var arr = xs.ToArray();
        if (arr.Length == 0) return 0;
        Array.Sort(arr);
        return arr[arr.Length / 2];
    }

    static (double? cpuW, double? gpuW, double? cpuT, double? gpuT, int? cpuMhz) LatestTelemetry(HardwareMonitor monitor)
    {
        var t = monitor.Latest();
        if (t is null) return (null, null, null, null, null);
        return (t.CpuPkgWatts, t.GpuWatts, t.CpuTempC, t.GpuTempC, t.CpuMhz);
    }
}
