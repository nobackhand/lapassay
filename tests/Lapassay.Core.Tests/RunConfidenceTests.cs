using Lapassay.Core;
using Lapassay.Core.Models;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers <see cref="RunConfidence.Assess"/> — the shared judgment shown beside the score
/// in the GUI, HTML, and CLI. Level is driven by Developer Mode (GPU clock locking) and
/// repeat count; admin and power source appear in the detail only.
/// </summary>
public class RunConfidenceTests
{
    [Fact]
    public void DevModeAndRepeatsIsHigh()
    {
        var (level, detail) = RunConfidence.Assess(new RunContext(IsAdmin: true, DeveloperMode: true, OnBattery: false, RepeatCount: 3));

        Assert.Equal("HIGH", level);
        Assert.Contains("GPU clocks locked", detail);
        Assert.Contains("N=3", detail);
        Assert.Contains("on AC", detail);
    }

    [Fact]
    public void DevModeAloneOrRepeatsAloneIsMedium()
    {
        var (devOnly, _) = RunConfidence.Assess(new RunContext(true, DeveloperMode: true, false, RepeatCount: 1));
        var (repeatsOnly, _) = RunConfidence.Assess(new RunContext(true, DeveloperMode: false, false, RepeatCount: 5));

        Assert.Equal("MEDIUM", devOnly);
        Assert.Equal("MEDIUM", repeatsOnly);
    }

    [Fact]
    public void NeitherIsLowAndUnlockedClocksAreCalledOut()
    {
        var (level, detail) = RunConfidence.Assess(new RunContext(false, DeveloperMode: false, OnBattery: true, RepeatCount: 1));

        Assert.Equal("LOW", level);
        Assert.Contains("GPU clocks unlocked", detail);
        Assert.Contains("on battery", detail);
        Assert.Contains("no power telemetry", detail);
    }

    [Fact]
    public void MissingContextIsUnknownNotLow()
    {
        var (level, _) = RunConfidence.Assess(null);

        Assert.Equal("UNKNOWN", level);
    }
}
