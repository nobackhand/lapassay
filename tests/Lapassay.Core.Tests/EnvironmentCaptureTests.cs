using System.Runtime.Versioning;
using Lapassay.Core.Telemetry;

namespace Lapassay.Core.Tests;

[SupportedOSPlatform("windows")]
public class EnvironmentCaptureTests
{
    [Fact]
    public void CaptureReturnsNonEmptyCoreFields()
    {
        var env = EnvironmentCapture.Capture();
        Assert.NotNull(env);
        Assert.False(string.IsNullOrWhiteSpace(env.Cpu.Model));
        Assert.True(env.Cpu.LogicalCores > 0);
        Assert.NotEmpty(env.Gpu);
        Assert.False(string.IsNullOrWhiteSpace(env.Gpu[0].Model));
        Assert.True(env.Ram.TotalGb > 0);
        Assert.False(string.IsNullOrWhiteSpace(env.Os.WindowsBuild));
    }
}
