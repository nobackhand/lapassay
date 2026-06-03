using Lapassay.Core.Models;
using Lapassay.Core.Reporting;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers <see cref="DiffService"/> — the shared loader/differ used by the CLI and every
/// GUI compare path. Pure logic (no Windows APIs), so these run on any platform. The
/// non-finite test is a direct regression guard: the compare paths used to build ad-hoc
/// <c>JsonSerializerOptions</c> without <c>AllowNamedFloatingPointLiterals</c> and threw
/// on a run file that legitimately serialized <c>Infinity</c>/<c>NaN</c>.
/// </summary>
public class DiffServiceTests
{
    static BenchmarkRun Run(string tag, double value, int score) => new(
        SchemaVersion: "1.0",
        Tool: "lapassay",
        ToolVersion: "0.6.0",
        RunId: $"2026-01-01T00:00:00Z-host-{tag}",
        Environment: new EnvironmentInfo(
            Cpu: new CpuInfo("CPU", 8, 16, 2400, 4800, 24),
            Gpu: new List<GpuInfo> { new("GPU", 8192, "1.0") },
            Ram: new RamInfo(32, 4800, 2),
            Os: new OsInfo("26200", "BIOS", "Balanced", false),
            CapturedAt: DateTimeOffset.UnixEpoch),
        Scores: new Scores(score, 0, score, new List<CategoryScore>()),
        Benchmarks: new List<BenchmarkResult>
        {
            new("cpu.sgemm.fp32.1024", "cpu", "gflops", value, score,
                new BenchmarkStats(10, value, 0, value, value), 1.0,
                new TelemetrySummary(null, null, null, null, null, null, null, null)),
        });

    static (string a, string b) WriteTemp(BenchmarkRun a, BenchmarkRun b)
    {
        var pa = Path.GetTempFileName();
        var pb = Path.GetTempFileName();
        File.WriteAllText(pa, JsonReport.Serialize(a));
        File.WriteAllText(pb, JsonReport.Serialize(b));
        return (pa, pb);
    }

    [Fact]
    public void BuildDiffPairsSharedBenchmarksAndComputesDeltas()
    {
        var (a, b) = WriteTemp(Run("a", 100.0, 1000), Run("b", 150.0, 1500));
        try
        {
            var cmp = DiffService.BuildDiff(a, b, "A", "B");

            Assert.Single(cmp.PerBenchmark);
            Assert.Equal(100.0, cmp.PerBenchmark[0].ValueA);
            Assert.Equal(150.0, cmp.PerBenchmark[0].ValueB);
            Assert.Equal(500, cmp.OverallScoreDelta);
        }
        finally { File.Delete(a); File.Delete(b); }
    }

    [Fact]
    public void BuildDiffLoadsRunsContainingNonFiniteValues()
    {
        var (a, b) = WriteTemp(Run("a", double.PositiveInfinity, 0), Run("b", 150.0, 1500));
        try
        {
            var cmp = DiffService.BuildDiff(a, b, "A", "B");

            Assert.Equal(double.PositiveInfinity, cmp.PerBenchmark[0].ValueA);
        }
        finally { File.Delete(a); File.Delete(b); }
    }
}
