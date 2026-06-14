using Lapassay.Core.Models;

namespace Lapassay.Core;

/// <summary>
/// Merges N independent <see cref="BenchmarkRun"/>s (produced by <c>--repeat</c>) into one:
/// each benchmark's <see cref="BenchmarkResult.Value"/> becomes the median across runs, with
/// p25/p75 recorded in <see cref="Repeats"/>. Scores are recomputed from the medians.
/// Environment and per-result telemetry are taken from the last run (documented behavior).
///
/// Pure and platform-agnostic on purpose — the Windows-only kernels run inside
/// <see cref="Runner.Run"/>; this aggregation can be unit-tested anywhere.
/// </summary>
public static class RepeatAggregation
{
    public static BenchmarkRun Merge(IReadOnlyList<BenchmarkRun> runs)
    {
        if (runs is null || runs.Count == 0)
            throw new ArgumentException("Need at least one run to aggregate.", nameof(runs));
        if (runs.Count == 1) return runs[0];

        var last = runs[^1];
        var merged = new List<BenchmarkResult>(last.Benchmarks.Count);
        foreach (var template in last.Benchmarks)
        {
            var values = new List<double>(runs.Count);
            foreach (var run in runs)
            {
                var match = run.Benchmarks.FirstOrDefault(b => b.Id == template.Id);
                if (match is not null) values.Add(match.Value);
            }
            var sorted = values.ToArray();
            Array.Sort(sorted);
            var repeats = new Repeats(sorted, Percentile(sorted, 50), Percentile(sorted, 25), Percentile(sorted, 75));
            merged.Add(template with { Value = repeats.Median, Repeats = repeats });
        }

        // Re-score from the medians so the headline numbers reflect the aggregate, not the last run.
        var scored = merged.Select(r => r with { Score = Scoring.Scoring.ScoreFor(r) }).ToList();
        var scores = Scoring.Scoring.Compute(scored);
        return last with { Benchmarks = scored, Scores = scores };
    }

    /// <summary>Linear-interpolated percentile (0–100) over a pre-sorted array.</summary>
    static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        var rank = p / 100.0 * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        var frac = rank - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }
}
