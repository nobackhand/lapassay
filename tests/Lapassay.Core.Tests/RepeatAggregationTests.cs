using Lapassay.Core;
using Lapassay.Core.Models;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers <see cref="RepeatAggregation.Merge"/> — the pure median/IQR aggregation behind
/// <c>--repeat</c>. Platform-agnostic (no kernels, no Windows APIs).
/// </summary>
public class RepeatAggregationTests
{
    static BenchmarkRun RunWith(params (string id, double value)[] benches) => new(
        SchemaVersion: "1.2",
        Tool: "lapassay",
        ToolVersion: "0.6.0",
        RunId: "2026-01-01T00:00:00Z-host-deadbeef",
        Environment: new EnvironmentInfo(
            Cpu: new CpuInfo("CPU", 8, 16, 2400, 4800, 24),
            Gpu: new List<GpuInfo> { new("GPU", 8192, "1.0") },
            Ram: new RamInfo(32, 4800, 2),
            Os: new OsInfo("26200", "BIOS", "Balanced", false),
            CapturedAt: DateTimeOffset.UnixEpoch),
        Scores: new Scores(0, 0, 0, new List<CategoryScore>()),
        Benchmarks: benches.Select(x => new BenchmarkResult(
            x.id, "cpu", "gflops", x.value, 0,
            new BenchmarkStats(1, x.value, 0, x.value, x.value), 1.0,
            new TelemetrySummary(null, null, null, null, null, null, null, null))).ToList());

    [Fact]
    public void SingleRunIsReturnedUnchanged()
    {
        var only = RunWith(("cpu.sgemm.fp32.1024", 100.0));
        Assert.Same(only, RepeatAggregation.Merge(new[] { only }));
    }

    [Fact]
    public void ValueBecomesMedianAndIqrIsRecorded()
    {
        var runs = new[]
        {
            RunWith(("cpu.sgemm.fp32.1024", 100.0)),
            RunWith(("cpu.sgemm.fp32.1024", 110.0)),
            RunWith(("cpu.sgemm.fp32.1024", 120.0)),
            RunWith(("cpu.sgemm.fp32.1024", 130.0)),
            RunWith(("cpu.sgemm.fp32.1024", 140.0)),
        };

        var merged = RepeatAggregation.Merge(runs);
        var b = merged.Benchmarks.Single();

        Assert.Equal(120.0, b.Value);                 // median of 100..140
        Assert.NotNull(b.Repeats);
        Assert.Equal(120.0, b.Repeats!.Median);
        Assert.Equal(110.0, b.Repeats.P25);           // linear-interpolated quartiles
        Assert.Equal(130.0, b.Repeats.P75);
        Assert.Equal(5, b.Repeats.Values.Length);
    }

    [Fact]
    public void RescoresFromMedians()
    {
        // sgemm baseline is 80 GFLOPS → score = 1000 * value/80. Median 160 → 2000.
        var runs = new[]
        {
            RunWith(("cpu.sgemm.fp32.1024", 80.0)),
            RunWith(("cpu.sgemm.fp32.1024", 160.0)),
            RunWith(("cpu.sgemm.fp32.1024", 240.0)),
        };

        var merged = RepeatAggregation.Merge(runs);

        Assert.Equal(160.0, merged.Benchmarks.Single().Value);
        Assert.Equal(2000, merged.Benchmarks.Single().Score);
    }
}
