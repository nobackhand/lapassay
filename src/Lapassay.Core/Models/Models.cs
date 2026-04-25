using System.Text.Json.Serialization;

namespace Lapassay.Core.Models;

public record CpuInfo(
    string Model,
    int PhysicalCores,
    int LogicalCores,
    int BaseClockMhz,
    int MaxTurboMhz,
    int L3CacheMb);

public record GpuInfo(
    string Model,
    int VramMb,
    string Driver);

public record RamInfo(
    int TotalGb,
    int SpeedMhz,
    int Channels);

public record OsInfo(
    string WindowsBuild,
    string Bios,
    string PowerPlan,
    bool OnBattery);

public record EnvironmentInfo(
    CpuInfo Cpu,
    List<GpuInfo> Gpu,
    RamInfo Ram,
    OsInfo Os,
    DateTimeOffset CapturedAt);

public record BenchmarkStats(
    int Iterations,
    double Median,
    double Stdev,
    double Min,
    double Max);

public record TelemetrySummary(
    double? CpuPkgWattsAvg,
    double? CpuPkgWattsMax,
    double? GpuWattsAvg,
    double? GpuWattsMax,
    double? CpuTempCMax,
    double? GpuTempCMax,
    int? CpuMhzMin,
    int? CpuMhzMax);

public record BenchmarkResult(
    string Id,
    string Kind,
    string Metric,
    double Value,
    int Score,
    BenchmarkStats Stats,
    double DurationSec,
    TelemetrySummary Telemetry);

public record CategoryScore(string Name, int Score, int BenchmarkCount);

/// <summary>Emitted by the Runner before each kernel starts so a UI can show "X of Y".</summary>
public record KernelProgress(string Id, int Index, int Total);

/// <summary>Aggregate scores: 1000 = mid-range 2024 laptop baseline.</summary>
public record Scores(int Cpu, int Gpu, int Overall, List<CategoryScore> Categories);

public record ScalingPoint(int Threads, double Gflops, double EfficiencyPct);

public record BenchmarkRun(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    string Tool,
    string ToolVersion,
    string RunId,
    EnvironmentInfo Environment,
    Scores Scores,
    List<BenchmarkResult> Benchmarks,
    List<ScalingPoint>? ScalingCurve = null);

// ---------- Sustained / throttle test ----------

public record SustainedSample(
    double ElapsedSec,
    double CpuGflops,
    double GpuGflops,
    double? CpuPkgWatts,
    double? GpuWatts,
    double? CpuTempC,
    double? GpuTempC,
    int? CpuMhz);

public record ThrottleVerdict(
    bool Throttled,
    double CpuDropPct,
    double GpuDropPct,
    double FirstWindowCpuGflops,
    double LastWindowCpuGflops,
    double FirstWindowGpuGflops,
    double LastWindowGpuGflops);

public record SustainedRun(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    string Tool,
    string ToolVersion,
    string RunId,
    EnvironmentInfo Environment,
    double DurationSec,
    int IterationCount,
    ThrottleVerdict Verdict,
    List<SustainedSample> Samples);
