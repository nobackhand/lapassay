using Lapassay.Core.Models;
using Lapassay.Core.Reporting;

namespace Lapassay.Core.Tests;

public class JsonReportTests
{
    [Fact]
    public void RoundtripPreservesAllFields()
    {
        var env = new EnvironmentInfo(
            Cpu: new CpuInfo("Test CPU", 8, 16, 2400, 4800, 24),
            Gpu: new List<GpuInfo> { new("Test GPU", 8192, "1.2.3") },
            Ram: new RamInfo(32, 4800, 2),
            Os: new OsInfo("26200.1", "BIOS-1.0", "Balanced", false),
            CapturedAt: new DateTimeOffset(2026, 4, 23, 14, 32, 0, TimeSpan.Zero));

        var bench = new BenchmarkResult(
            Id: "cpu.sgemm.fp32.1024",
            Kind: "cpu",
            Metric: "gflops",
            Value: 412.7,
            Score: 5158,
            Stats: new BenchmarkStats(10, 412.7, 3.1, 408.2, 418.9),
            DurationSec: 12.4,
            Telemetry: new TelemetrySummary(38.2, 42.5, 15.0, 20.0, 78, 70, 3200, 4200));

        var run = new BenchmarkRun(
            SchemaVersion: "1.0",
            Tool: "lapassay",
            ToolVersion: "0.3.0",
            RunId: "test-run-1",
            Environment: env,
            Scores: new Scores(5158, 0, 5158, new List<CategoryScore>()),
            Benchmarks: new List<BenchmarkResult> { bench });

        var json = JsonReport.Serialize(run);
        var deserialized = JsonReport.Deserialize(json);

        Assert.Equal(run.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(run.Tool, deserialized.Tool);
        Assert.Equal(run.Environment.Cpu.Model, deserialized.Environment.Cpu.Model);
        Assert.Equal(run.Environment.Cpu.PhysicalCores, deserialized.Environment.Cpu.PhysicalCores);
        Assert.Equal(run.Environment.Gpu[0].Model, deserialized.Environment.Gpu[0].Model);
        Assert.Single(deserialized.Benchmarks);
        Assert.Equal(bench.Value, deserialized.Benchmarks[0].Value);
        Assert.Equal(bench.Stats.Iterations, deserialized.Benchmarks[0].Stats.Iterations);
        Assert.Equal(bench.Telemetry.CpuPkgWattsAvg, deserialized.Benchmarks[0].Telemetry.CpuPkgWattsAvg);
    }

    [Fact]
    public void DefaultPathIsTimestampedUnderResults()
    {
        var p = JsonReport.DefaultPath();
        Assert.StartsWith("results", p);
        Assert.EndsWith(".json", p);
    }
}
