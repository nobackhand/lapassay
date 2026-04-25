using Lapassay.Core.Models;

namespace Lapassay.Core.Reporting;

public record BenchmarkDelta(
    string Id,
    string Kind,
    string Metric,
    double ValueA,
    double ValueB,
    int ScoreA,
    int ScoreB,
    bool HigherIsBetter)
{
    public double DeltaAbs => ValueB - ValueA;
    public double DeltaPct => ValueA != 0 ? (ValueB - ValueA) / ValueA * 100 : 0;
    public int ScoreDelta => ScoreB - ScoreA;

    /// <summary>+1 = B improved, -1 = B regressed, 0 = no meaningful change (&lt;1%).</summary>
    public int Direction
    {
        get
        {
            if (Math.Abs(DeltaPct) < 1.0) return 0;
            var improved = HigherIsBetter ? DeltaPct > 0 : DeltaPct < 0;
            return improved ? +1 : -1;
        }
    }
}

public record RunComparison(
    string LabelA,
    string LabelB,
    BenchmarkRun RunA,
    BenchmarkRun RunB,
    IReadOnlyList<BenchmarkDelta> PerBenchmark,
    int OverallScoreDelta,
    int CpuScoreDelta,
    int GpuScoreDelta);

public static class Compare
{
    /// <summary>Lower-is-better metric ids. Anything not in this set is treated as higher-is-better.</summary>
    static readonly HashSet<string> LowerIsBetter = new(StringComparer.OrdinalIgnoreCase)
    {
        "cpu.latency.pointerchase",
    };

    public static RunComparison Diff(BenchmarkRun a, BenchmarkRun b, string? labelA = null, string? labelB = null)
    {
        // Pair benchmarks by id; only include those present in both.
        var byIdA = a.Benchmarks.ToDictionary(x => x.Id);
        var byIdB = b.Benchmarks.ToDictionary(x => x.Id);
        var sharedIds = byIdA.Keys.Intersect(byIdB.Keys).ToList();

        // Order: kind (cpu before gpu), then by id alphabetically — stable, scannable.
        sharedIds.Sort((x, y) =>
        {
            var ka = byIdA[x].Kind;
            var kb = byIdA[y].Kind;
            if (ka != kb) return string.Compare(ka, kb, StringComparison.Ordinal);
            return string.Compare(x, y, StringComparison.Ordinal);
        });

        var deltas = sharedIds.Select(id =>
        {
            var ra = byIdA[id];
            var rb = byIdB[id];
            return new BenchmarkDelta(
                Id: id,
                Kind: ra.Kind,
                Metric: ra.Metric,
                ValueA: ra.Value,
                ValueB: rb.Value,
                ScoreA: ra.Score,
                ScoreB: rb.Score,
                HigherIsBetter: !LowerIsBetter.Contains(id));
        }).ToList();

        return new RunComparison(
            LabelA: labelA ?? "A",
            LabelB: labelB ?? "B",
            RunA: a,
            RunB: b,
            PerBenchmark: deltas,
            OverallScoreDelta: b.Scores.Overall - a.Scores.Overall,
            CpuScoreDelta: b.Scores.Cpu - a.Scores.Cpu,
            GpuScoreDelta: b.Scores.Gpu - a.Scores.Gpu);
    }
}
