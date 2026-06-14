using Lapassay.Core.Models;
using Lapassay.Core.Reporting;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers <see cref="Compare.Diff"/>, focusing on the v1.2 legacy id mapping that lets a
/// pre-rename run (<c>gpu.matmul.fp16.N</c>) diff against a renamed one
/// (<c>gpu.matmul.fp16alu.N</c>) without silently dropping the benchmark.
/// </summary>
public class CompareTests
{
    static BenchmarkRun RunWith(string benchId, double value) => new(
        SchemaVersion: "1.2",
        Tool: "lapassay",
        ToolVersion: "0.6.0",
        RunId: "2026-01-01T00:00:00Z-host-aaaaaaaa",
        Environment: new EnvironmentInfo(
            Cpu: new CpuInfo("CPU", 8, 16, 2400, 4800, 24),
            Gpu: new List<GpuInfo> { new("GPU", 8192, "1.0") },
            Ram: new RamInfo(32, 4800, 2),
            Os: new OsInfo("26200", "BIOS", "Balanced", false),
            CapturedAt: DateTimeOffset.UnixEpoch),
        Scores: new Scores(0, 1000, 1000, new List<CategoryScore>()),
        Benchmarks: new List<BenchmarkResult>
        {
            new(benchId, "gpu", "gflops", value, 1000,
                new BenchmarkStats(10, value, 0, value, value), 1.0,
                new TelemetrySummary(null, null, null, null, null, null, null, null)),
        });

    [Fact]
    public void LegacyFp16IdPairsWithRenamedFp16AluId()
    {
        var oldRun = RunWith("gpu.matmul.fp16.2048", 900.0);
        var newRun = RunWith("gpu.matmul.fp16alu.2048", 1100.0);

        var cmp = Compare.Diff(oldRun, newRun, "old", "new");

        var d = Assert.Single(cmp.PerBenchmark);
        Assert.Equal("gpu.matmul.fp16alu.2048", d.Id);
        Assert.Equal(900.0, d.ValueA);
        Assert.Equal(1100.0, d.ValueB);
    }
}
