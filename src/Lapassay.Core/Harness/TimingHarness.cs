using System.Diagnostics;
using Lapassay.Core.Models;

namespace Lapassay.Core.Harness;

/// <summary>
/// Lightweight in-process timing harness — warmup iterations followed by
/// measurement iterations, reporting median/stdev/min/max.
/// For a shipping benchmark app this is preferable to BenchmarkDotNet's
/// process-forking model.
/// </summary>
public static class TimingHarness
{
    public record RunResult(double MedianSeconds, BenchmarkStats Stats, double TotalSec);

    /// <summary>Run `action` for warmup iterations, then timed iterations.
    /// Forces a full GC between warmup and measurement so a mid-run collection
    /// doesn't poison the timings.</summary>
    public static RunResult Run(Action action, int warmup = 5, int measure = 10)
    {
        for (var i = 0; i < warmup; i++) action();

        // Ensure any garbage from warmup is collected before we start timing.
        // Without this, a Gen2 collection in the middle of measurement blows up stdev.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var samples = new double[measure];
        var overall = Stopwatch.StartNew();
        for (var i = 0; i < measure; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalSeconds;
        }
        overall.Stop();

        Array.Sort(samples);
        var median = samples[measure / 2];
        var min = samples[0];
        var max = samples[measure - 1];

        // Trimmed stdev: drop the top 10% of samples before computing dispersion.
        // Protects the metric from single-iteration GC/interrupt pauses.
        var trimCount = Math.Max(1, measure / 10);
        var keep = measure - trimCount;
        var trimmed = new double[keep];
        Array.Copy(samples, 0, trimmed, 0, keep);
        var trimmedMean = trimmed.Average();
        var variance = trimmed.Sum(s => (s - trimmedMean) * (s - trimmedMean)) / keep;
        var stdev = Math.Sqrt(variance);

        return new RunResult(
            MedianSeconds: median,
            Stats: new BenchmarkStats(measure, median, stdev, min, max),
            TotalSec: overall.Elapsed.TotalSeconds);
    }
}

/// <summary>
/// Anti-dead-code-elimination sink. The CLR can't prove the sink value is
/// unused, so it can't optimize the computation away.
/// </summary>
public static class Sink
{
    public static volatile int IntSink;
    public static double DoubleSink;

    public static void Consume(double v) => DoubleSink = v;
    public static void Consume(float v) => DoubleSink = v;
    public static void Consume(int v) => IntSink = v;
    public static void Consume(ReadOnlySpan<float> span)
    {
        double acc = 0;
        for (var i = 0; i < span.Length; i += 64) acc += span[i];
        DoubleSink = acc;
    }
}
