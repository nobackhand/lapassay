using System.Diagnostics;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;
using Lapassay.Core.Kernels.Cpu;
using Lapassay.Core.Kernels.Gpu;
using Lapassay.Core.Models;
using Lapassay.Core.Scoring;
using Lapassay.Core.Telemetry;

namespace Lapassay.Core;

[SupportedOSPlatform("windows")]
public static class Runner
{
    public record RunOptions(bool Cpu, bool Gpu, int CpuN = 1024, int GpuN = 2048,
        Action<TelemetrySample>? OnTelemetrySample = null,
        Action<KernelProgress>? OnKernelStart = null);

    // Warmup/measurement counts per kernel category. Tuned empirically; CPU
    // benches need more warmup on laptops with aggressive turbo/DVFS.
    const int CpuWarmup = 8;
    const int CpuMeasure = 15;

    public static BenchmarkRun Run(RunOptions options, Action<string>? log = null)
    {
        log ??= _ => { };
        log("Capturing environment...");
        var env = EnvironmentCapture.Capture();

        var results = new List<BenchmarkResult>();
        List<ScalingPoint>? scalingCurve = null;

        // A single global monitor spans the whole run so the live chart shows a continuous
        // timeline across all kernels. Per-kernel monitors still run inside RunCpuBench /
        // RunGpu… for the per-result Telemetry summary; they don't conflict — both just read
        // the same sensors at slightly different cadences.
        HardwareMonitor? globalMonitor = null;
        if (options.OnTelemetrySample is not null)
        {
            globalMonitor = new HardwareMonitor(intervalMs: 250);
            globalMonitor.OnSample = options.OnTelemetrySample;
            globalMonitor.Start();
        }

        // Total kernels we'll fire (matches the per-branch counts below).
        var totalKernels = (options.Cpu ? 9 : 0) + (options.Gpu ? 3 : 0);
        var kernelIndex = 0;
        void Begin(string id)
        {
            kernelIndex++;
            options.OnKernelStart?.Invoke(new KernelProgress(id, kernelIndex, totalKernels));
        }

        if (options.Cpu)
        {
            // Pin the orchestrating thread to CPU 0. Multi-threaded kernels use
            // Parallel.For and can still span all cores; single-threaded kernels
            // benefit from a stable home core.
            ThreadPinning.PinCurrentThread(0);

            Begin($"cpu.sgemm.fp32.{options.CpuN}"); results.Add(RunCpuSgemm(options.CpuN, log));
            Begin("cpu.aes128cbc");                  results.Add(RunAes(log));
            Begin("cpu.sha256");                     results.Add(RunSha256(log));
            Begin("cpu.zstd.level3");                results.Add(RunZstd(log));
            Begin("cpu.fft.c2c.4096");               results.Add(RunFft(log));
            Begin("cpu.mandelbrot.2048");            results.Add(RunMandelbrot(log));
            Begin("cpu.stream.triad");               results.Add(RunStreamTriad(log));
            Begin("cpu.latency.pointerchase");       results.Add(RunPointerChase(log));
            Begin("cpu.scaling.efficiency");
            var (scalingResult, curve) = RunCpuScaling(env.Cpu.PhysicalCores, log);
            results.Add(scalingResult);
            scalingCurve = curve;
        }

        if (options.Gpu)
        {
            Begin($"gpu.matmul.fp32.{options.GpuN}"); results.Add(RunGpuFp32Matmul(options.GpuN, log));
            Begin($"gpu.matmul.fp16.{options.GpuN}"); results.Add(RunGpuFp16Matmul(options.GpuN, log));
            Begin("gpu.ai.squeezenet");               results.Add(RunOnnxSqueezenet(log));
        }

        // Stop the global telemetry stream now that all kernels are done.
        if (globalMonitor is not null)
        {
            globalMonitor.Stop();
            globalMonitor.Dispose();
        }

        // Attach per-benchmark score.
        var scored = results.Select(r => r with { Score = Scoring.Scoring.ScoreFor(r) }).ToList();
        var scores = Scoring.Scoring.Compute(scored);

        log("");
        log($"Overall score: {scores.Overall}");
        if (scores.Cpu > 0) log($"  CPU: {scores.Cpu}");
        if (scores.Gpu > 0) log($"  GPU: {scores.Gpu}");
        log("  (baseline: mid-range 2024 laptop ≈ 1000)");

        var runId = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}-{Environment.MachineName.ToLowerInvariant()}-{Guid.NewGuid().ToString()[..8]}";
        return new BenchmarkRun(
            SchemaVersion: "1.0",
            Tool: "lapassay",
            ToolVersion: "0.6.0",
            RunId: runId,
            Environment: env,
            Scores: scores,
            Benchmarks: scored,
            ScalingCurve: scalingCurve);
    }

    /// <summary>Generic CPU bench runner — times `run` with warmup/measurement, captures telemetry, builds BenchmarkResult.</summary>
    static BenchmarkResult RunCpuBench(
        string id, string metric, Action run, Func<double, double> rate, Action<string> log, string summaryUnit)
    {
        using var monitor = new HardwareMonitor();
        monitor.Start();
        var timing = TimingHarness.Run(run, warmup: CpuWarmup, measure: CpuMeasure);
        var samples = monitor.Stop();

        var primary = rate(timing.MedianSeconds);
        log($"  {id}: {primary:F1} {summaryUnit} (median {timing.MedianSeconds * 1000:F1}ms, stdev {timing.Stats.Stdev * 1000:F2}ms)");

        var relStdev = timing.Stats.Median != 0 ? timing.Stats.Stdev / timing.Stats.Median : 0;
        return new BenchmarkResult(
            Id: id,
            Kind: "cpu",
            Metric: metric,
            Value: primary,
            Score: 0, // filled in after all benchmarks run
            Stats: new BenchmarkStats(
                Iterations: timing.Stats.Iterations,
                Median: primary,
                Stdev: primary * relStdev,
                Min: rate(timing.Stats.Max),
                Max: rate(timing.Stats.Min)),
            DurationSec: timing.TotalSec,
            Telemetry: HardwareMonitor.Summarize(samples));
    }

    static BenchmarkResult RunCpuSgemm(int n, Action<string> log)
    {
        log($"Running cpu.sgemm.fp32.{n}...");
        var k = new SgemmKernel(n);
        return RunCpuBench($"cpu.sgemm.fp32.{n}", "gflops", k.Run, k.Gflops, log, "GFLOPS");
    }

    static BenchmarkResult RunAes(Action<string> log)
    {
        log("Running cpu.aes128cbc...");
        using var k = new AesKernel();
        return RunCpuBench("cpu.aes128cbc", "mb/s", k.Run, k.MegabytesPerSecond, log, "MB/s");
    }

    static BenchmarkResult RunSha256(Action<string> log)
    {
        log("Running cpu.sha256...");
        var k = new Sha256Kernel();
        return RunCpuBench("cpu.sha256", "mb/s", k.Run, k.MegabytesPerSecond, log, "MB/s");
    }

    static BenchmarkResult RunZstd(Action<string> log)
    {
        log("Running cpu.zstd.level3...");
        using var k = new ZstdKernel();
        return RunCpuBench("cpu.zstd.level3", "mb/s", k.Run, k.MegabytesPerSecond, log, "MB/s");
    }

    static BenchmarkResult RunFft(Action<string> log)
    {
        log("Running cpu.fft.c2c.4096...");
        var k = new FftKernel(4096);
        return RunCpuBench("cpu.fft.c2c.4096", "mflops", k.Run, k.Mflops, log, "MFLOPS");
    }

    static BenchmarkResult RunMandelbrot(Action<string> log)
    {
        log("Running cpu.mandelbrot.2048...");
        var k = new MandelbrotKernel();
        return RunCpuBench("cpu.mandelbrot.2048", "mpix/s", k.Run, k.MegapixelsPerSecond, log, "Mpix/s");
    }

    static BenchmarkResult RunStreamTriad(Action<string> log)
    {
        log("Running cpu.stream.triad...");
        var k = new StreamTriadKernel();
        return RunCpuBench("cpu.stream.triad", "gb/s", k.Run, k.GbPerSecond, log, "GB/s");
    }

    static BenchmarkResult RunPointerChase(Action<string> log)
    {
        log("Running cpu.latency.pointerchase...");
        var k = new PointerChaseKernel();
        // Lower is better; we report ns/access.
        return RunCpuBench("cpu.latency.pointerchase", "ns/access", k.Run, k.NanosecondsPerAccess, log, "ns/access");
    }

    static (BenchmarkResult result, List<ScalingPoint> curve) RunCpuScaling(int physicalCores, Action<string> log)
    {
        log("Running cpu.scaling.efficiency...");
        var sw = Stopwatch.StartNew();
        using var monitor = new HardwareMonitor();
        monitor.Start();
        var kernel = new CpuScalingKernel(n: 768, physicalCores);
        var curve = kernel.RunSweep(warmupIterations: 1, measurementIterations: 3);
        var samples = monitor.Stop();
        sw.Stop();

        foreach (var p in curve)
            log($"    {p.Threads,2} thread{(p.Threads == 1 ? " " : "s")}: {p.Gflops,6:F1} GFLOPS  ({p.EfficiencyPct,5:F1}% of ideal)");

        var efficiency = CpuScalingKernel.EfficiencyAtFull(curve);
        log($"  cpu.scaling.efficiency: {efficiency:F1}% at {physicalCores} cores");

        return (new BenchmarkResult(
            Id: "cpu.scaling.efficiency",
            Kind: "cpu",
            Metric: "%",
            Value: efficiency,
            Score: 0,
            Stats: new BenchmarkStats(curve.Count, efficiency, 0, curve[0].EfficiencyPct, efficiency),
            DurationSec: sw.Elapsed.TotalSeconds,
            Telemetry: HardwareMonitor.Summarize(samples)),
            curve);
    }

    static D3D12Context MakeGpuContext(Action<string> log)
    {
        var debug = Environment.GetEnvironmentVariable("LAPASSAY_D3D12_DEBUG") == "1";
        var preflight = Preflight.Check();
        var ctx = new D3D12Context(enableDebug: debug, enableStablePowerState: preflight.DeveloperMode);
        if (!ctx.StablePowerStateEnabled)
            log("  (Dev Mode off: GPU clocks unlocked — ~10-30% variance)");
        log($"  GPU adapter: {ctx.AdapterName}");
        return ctx;
    }

    /// <summary>Generic GPU-matmul bench runner — handles both FP32 and FP16 kernels.</summary>
    static BenchmarkResult RunGpuMatmulBench(string id, int n, Func<D3D12Context, IDisposable> makeKernel,
        Func<IDisposable, double[]> measure, Func<IDisposable, double, double> gflops, Action<string> log)
    {
        log($"Running {id}...");
        using var ctx = MakeGpuContext(log);

        using var monitor = new HardwareMonitor();
        using var kernel = makeKernel(ctx);

        var totalSw = Stopwatch.StartNew();
        monitor.Start();
        var times = measure(kernel);
        var samples = monitor.Stop();
        totalSw.Stop();

        Array.Sort(times);
        var median = times[times.Length / 2];
        var min = times[0];
        var max = times[^1];
        var mean = times.Average();
        var variance = times.Sum(t => (t - mean) * (t - mean)) / times.Length;
        var stdev = Math.Sqrt(variance);

        var rate = gflops(kernel, median);
        log($"  {id}: {rate:F0} GFLOPS (median {median * 1000:F1}ms, stdev {stdev * 1000:F2}ms)");

        return new BenchmarkResult(
            Id: id,
            Kind: "gpu",
            Metric: "gflops",
            Value: rate,
            Score: 0,
            Stats: new BenchmarkStats(
                times.Length, Median: rate, Stdev: rate * (stdev / median),
                Min: gflops(kernel, max), Max: gflops(kernel, min)),
            DurationSec: totalSw.Elapsed.TotalSeconds,
            Telemetry: HardwareMonitor.Summarize(samples));
    }

    static BenchmarkResult RunGpuFp32Matmul(int n, Action<string> log) =>
        RunGpuMatmulBench(
            $"gpu.matmul.fp32.{n}", n,
            ctx => new Fp32MatmulKernel(ctx, n, measurementIterations: 10),
            k => ((Fp32MatmulKernel)k).Measure(warmupIterations: 5),
            (k, s) => ((Fp32MatmulKernel)k).Gflops(s),
            log);

    static BenchmarkResult RunGpuFp16Matmul(int n, Action<string> log) =>
        RunGpuMatmulBench(
            $"gpu.matmul.fp16.{n}", n,
            ctx => new Fp16MatmulKernel(ctx, n, measurementIterations: 10),
            k => ((Fp16MatmulKernel)k).Measure(warmupIterations: 5),
            (k, s) => ((Fp16MatmulKernel)k).Gflops(s),
            log);

    static BenchmarkResult RunOnnxSqueezenet(Action<string> log)
    {
        log("Running gpu.ai.squeezenet...");
        using var monitor = new HardwareMonitor();
        OnnxInferenceKernel? kernel;
        try
        {
            kernel = new OnnxInferenceKernel();
        }
        catch (FileNotFoundException ex)
        {
            log($"  SKIP gpu.ai.squeezenet: {ex.Message}");
            return new BenchmarkResult("gpu.ai.squeezenet", "gpu", "inf/s", 0, 0,
                new BenchmarkStats(0, 0, 0, 0, 0), 0,
                new TelemetrySummary(null, null, null, null, null, null, null, null));
        }

        using (kernel)
        {
            monitor.Start();
            var timing = TimingHarness.Run(kernel.Run, warmup: 10, measure: 30);
            var samples = monitor.Stop();

            var ips = 1.0 / timing.MedianSeconds;
            log($"  gpu.ai.squeezenet: {ips:F0} inf/s (median {timing.MedianSeconds * 1000:F2}ms/inference)");

            var relStdev = timing.Stats.Median != 0 ? timing.Stats.Stdev / timing.Stats.Median : 0;
            return new BenchmarkResult(
                Id: "gpu.ai.squeezenet",
                Kind: "gpu",
                Metric: "inf/s",
                Value: ips,
                Score: 0,
                Stats: new BenchmarkStats(
                    Iterations: timing.Stats.Iterations,
                    Median: ips,
                    Stdev: ips * relStdev,
                    Min: 1.0 / timing.Stats.Max,
                    Max: 1.0 / timing.Stats.Min),
                DurationSec: timing.TotalSec,
                Telemetry: HardwareMonitor.Summarize(samples));
        }
    }
}
