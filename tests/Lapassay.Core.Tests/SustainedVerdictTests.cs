using Lapassay.Core.Models;
using Lapassay.Core.Sustained;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers <see cref="SustainedRunner.ComputeVerdict"/> — the pure first-window vs
/// last-window throttle decision (the only part of the sustained runner that doesn't
/// need real hardware).
/// </summary>
public class SustainedVerdictTests
{
    static SustainedSample At(double t, double cpuGflops, double gpuGflops = 100.0) =>
        new(t, cpuGflops, gpuGflops, null, null, null, null, null);

    static List<SustainedSample> Timeline(double durationSec, Func<double, double> cpuAt)
    {
        var samples = new List<SustainedSample>();
        for (var t = 0.0; t < durationSec; t += 5.0)
            samples.Add(At(t, cpuAt(t)));
        return samples;
    }

    [Fact]
    public void SteadyThroughputIsNotThrottled()
    {
        var samples = Timeline(300, _ => 100.0);

        var v = SustainedRunner.ComputeVerdict(samples, totalDurationSec: 300);

        Assert.False(v.Throttled);
        Assert.Equal(100.0, v.FirstWindowCpuGflops);
        Assert.Equal(100.0, v.LastWindowCpuGflops);
    }

    [Fact]
    public void TenPercentCpuDropIsFlagged()
    {
        // 100 GFLOPS for the first 30s, 90 after — a 10% drop, above the 5% threshold.
        var samples = Timeline(300, t => t < 30 ? 100.0 : 90.0);

        var v = SustainedRunner.ComputeVerdict(samples, totalDurationSec: 300);

        Assert.True(v.Throttled);
        Assert.Equal(10.0, v.CpuDropPct, precision: 5);
    }

    [Fact]
    public void DropWithinThresholdPasses()
    {
        var samples = Timeline(300, t => t < 30 ? 100.0 : 97.0); // 3% < 5% threshold

        var v = SustainedRunner.ComputeVerdict(samples, totalDurationSec: 300);

        Assert.False(v.Throttled);
    }

    [Fact]
    public void EmptyOrTruncatedRunsReturnANoVerdict()
    {
        var v = SustainedRunner.ComputeVerdict(new List<SustainedSample>(), totalDurationSec: 0);

        Assert.False(v.Throttled);
        Assert.Equal(0, v.CpuDropPct);
    }
}
