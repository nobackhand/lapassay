using Lapassay.Core.Models;

namespace Lapassay.Core.Scoring;

/// <summary>
/// Normalizes raw benchmark values into 1000-point scores, geometric-mean-aggregated
/// per category. Baseline values represent a mid-range 2024 laptop (AMD Ryzen 7 7840U /
/// Intel Core Ultra 7 155H class, with a typical iGPU like Radeon 780M). Such a machine
/// should land around 900-1100 overall.
///
/// Per-benchmark score:
///   higher-is-better metrics: score = 1000 * (value / baseline)
///   latency metrics: score = 1000 * (baseline / value)
///
/// Category score: geometric mean of the per-benchmark scores in that category.
/// Overall score: geometric mean of CPU and GPU category scores.
/// </summary>
public static class Scoring
{
    record Baseline(double Value, bool HigherIsBetter);

    static readonly Dictionary<string, Baseline> Baselines = new()
    {
        // CPU integer / crypto
        ["cpu.aes128cbc"]            = new(3000.0, true),   // MB/s — AES-NI
        ["cpu.sha256"]               = new(2500.0, true),   // MB/s — SHA-NI
        ["cpu.zstd.level3"]          = new(500.0,  true),   // MB/s — single-threaded compression
        // CPU float / SIMD
        ["cpu.sgemm.fp32.1024"]      = new(80.0,   true),   // GFLOPS
        ["cpu.fft.c2c.4096"]         = new(2500.0, true),   // MFLOPS
        ["cpu.mandelbrot.2048"]      = new(300.0,  true),   // Mpix/s
        // Memory
        ["cpu.stream.triad"]         = new(45.0,   true),   // GB/s — DDR5 dual-channel class
        ["cpu.latency.pointerchase"] = new(90.0,   false),  // ns/access — lower is better
        ["cpu.scaling.efficiency"]   = new(70.0,   true),   // % — typical mid-range laptop scales 70%
        // GPU
        ["gpu.matmul.fp32.2048"]     = new(1000.0, true),   // GFLOPS — decent iGPU / entry dGPU
        ["gpu.matmul.fp16.2048"]     = new(2000.0, true),   // GFLOPS — ~2x FP32 on FP16-capable HW
        ["gpu.ai.squeezenet"]        = new(500.0,  true),   // inferences/sec — SqueezeNet via DirectML
    };

    /// <summary>Maps benchmark id → category. Categories aggregate via geometric mean.</summary>
    static readonly Dictionary<string, string> CategoryMap = new()
    {
        ["cpu.aes128cbc"]            = "cpu.integer",
        ["cpu.sha256"]               = "cpu.integer",
        ["cpu.zstd.level3"]          = "cpu.integer",
        ["cpu.sgemm.fp32.1024"]      = "cpu.float",
        ["cpu.fft.c2c.4096"]         = "cpu.float",
        ["cpu.mandelbrot.2048"]      = "cpu.float",
        ["cpu.stream.triad"]         = "cpu.memory",
        ["cpu.latency.pointerchase"] = "cpu.memory",
        ["cpu.scaling.efficiency"]   = "cpu.parallel",
        ["gpu.matmul.fp32.2048"]     = "gpu.compute",
        ["gpu.matmul.fp16.2048"]     = "gpu.compute",
        ["gpu.ai.squeezenet"]        = "gpu.ai",
    };

    /// <summary>Stable display order for categories.</summary>
    static readonly string[] CategoryOrder =
        { "cpu.integer", "cpu.float", "cpu.memory", "cpu.parallel", "gpu.compute", "gpu.ai" };

    public static int ScoreFor(BenchmarkResult r)
    {
        if (!Baselines.TryGetValue(r.Id, out var b)) return 0;
        if (r.Value <= 0) return 0;
        var ratio = b.HigherIsBetter ? r.Value / b.Value : b.Value / r.Value;
        return (int)Math.Round(ratio * 1000);
    }

    public static Scores Compute(IEnumerable<BenchmarkResult> results)
    {
        var list = results.ToList();
        var cpuRaw = list.Where(r => r.Kind == "cpu").Select(r => (double)ScoreFor(r)).Where(s => s > 0).ToArray();
        var gpuRaw = list.Where(r => r.Kind == "gpu").Select(r => (double)ScoreFor(r)).Where(s => s > 0).ToArray();

        var cpu = cpuRaw.Length == 0 ? 0 : (int)Math.Round(GeometricMean(cpuRaw));
        var gpu = gpuRaw.Length == 0 ? 0 : (int)Math.Round(GeometricMean(gpuRaw));

        int overall;
        if (cpu > 0 && gpu > 0) overall = (int)Math.Round(GeometricMean(new[] { (double)cpu, gpu }));
        else if (cpu > 0)       overall = cpu;
        else                    overall = gpu;

        // Category subscores (geomean within each category, only non-empty categories shown).
        var byCategory = new Dictionary<string, List<double>>();
        foreach (var r in list)
        {
            if (!CategoryMap.TryGetValue(r.Id, out var cat)) continue;
            var s = ScoreFor(r);
            if (s <= 0) continue;
            if (!byCategory.TryGetValue(cat, out var bucket)) byCategory[cat] = bucket = new List<double>();
            bucket.Add(s);
        }
        var categories = new List<CategoryScore>();
        foreach (var name in CategoryOrder)
        {
            if (!byCategory.TryGetValue(name, out var scores) || scores.Count == 0) continue;
            categories.Add(new CategoryScore(name, (int)Math.Round(GeometricMean(scores.ToArray())), scores.Count));
        }

        return new Scores(cpu, gpu, overall, categories);
    }

    static double GeometricMean(double[] values)
    {
        var sumLog = 0.0;
        foreach (var v in values) sumLog += Math.Log(v);
        return Math.Exp(sumLog / values.Length);
    }
}
