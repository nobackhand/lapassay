using Lapassay.Core.Models;
using Lapassay.Core.Scoring;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers the pure scoring math — baseline normalization (both directions) and the
/// geometric-mean aggregation, including the documented-but-subtle behavior that
/// zero-score benchmarks (e.g. a skipped SqueezeNet) are silently excluded from the
/// geomean basis.
/// </summary>
public class ScoringTests
{
    static BenchmarkResult Result(string id, string kind, double value) => new(
        id, kind, "unit", value, 0,
        new BenchmarkStats(10, value, 0, value, value), 1.0,
        new TelemetrySummary(null, null, null, null, null, null, null, null));

    [Fact]
    public void HigherIsBetterScoresScaleLinearlyAgainstBaseline()
    {
        // aes baseline = 3000 MB/s → exactly baseline = 1000, double = 2000.
        Assert.Equal(1000, Scoring.Scoring.ScoreFor(Result("cpu.aes128cbc", "cpu", 3000.0)));
        Assert.Equal(2000, Scoring.Scoring.ScoreFor(Result("cpu.aes128cbc", "cpu", 6000.0)));
    }

    [Fact]
    public void LowerIsBetterScoresInvert()
    {
        // pointer-chase baseline = 90 ns, lower is better → 45 ns = 2000, 180 ns = 500.
        Assert.Equal(2000, Scoring.Scoring.ScoreFor(Result("cpu.latency.pointerchase", "cpu", 45.0)));
        Assert.Equal(500, Scoring.Scoring.ScoreFor(Result("cpu.latency.pointerchase", "cpu", 180.0)));
    }

    [Fact]
    public void UnknownIdsAndNonPositiveValuesScoreZero()
    {
        Assert.Equal(0, Scoring.Scoring.ScoreFor(Result("cpu.not.a.benchmark", "cpu", 100.0)));
        Assert.Equal(0, Scoring.Scoring.ScoreFor(Result("cpu.aes128cbc", "cpu", 0.0)));
    }

    [Fact]
    public void OverallIsGeometricMeanOfCpuAndGpu()
    {
        var results = new[]
        {
            Result("cpu.aes128cbc", "cpu", 3000.0),        // cpu score 1000
            Result("gpu.matmul.fp32.2048", "gpu", 4000.0), // gpu baseline 1000 → score 4000
        };

        var scores = Scoring.Scoring.Compute(results);

        Assert.Equal(1000, scores.Cpu);
        Assert.Equal(4000, scores.Gpu);
        Assert.Equal(2000, scores.Overall); // sqrt(1000 * 4000)
    }

    [Fact]
    public void SkippedBenchmarksAreExcludedFromTheGeomeanBasis()
    {
        // A skipped kernel (value 0 → score 0) silently narrows the basis: the GPU score
        // here is the geomean of ONE kernel, not three. This test documents that behavior —
        // if it ever changes (e.g. to penalize skips), this should fail loudly.
        var results = new[]
        {
            Result("gpu.matmul.fp32.2048", "gpu", 2000.0), // score 2000
            Result("gpu.ai.squeezenet", "gpu", 0.0),       // skipped → excluded
        };

        var scores = Scoring.Scoring.Compute(results);

        Assert.Equal(2000, scores.Gpu);
    }
}
