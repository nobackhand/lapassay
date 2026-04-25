using System.Runtime.Versioning;
using Lapassay.Core.Kernels.Cpu;

namespace Lapassay.Core.Tests;

[SupportedOSPlatform("windows")]
public class SgemmKernelTests
{
    [Fact]
    public void MatchesNaiveReferenceForSmallN()
    {
        // 64x64 is large enough to exercise SIMD and the parallel loop.
        var k = new SgemmKernel(n: 64, seed: 7);
        k.Run();

        // Re-derive the inputs the kernel built (same seed, same rules).
        var rng = new Random(7);
        var n = k.N;
        var a = new float[n * n];
        var b = new float[n * n];
        for (var i = 0; i < a.Length; i++) a[i] = (float)(rng.NextDouble() * 2 - 1);
        for (var i = 0; i < b.Length; i++) b[i] = (float)(rng.NextDouble() * 2 - 1);

        var expected = SgemmKernel.NaiveReference(a, b, n);

        // Kernel stores its own copy of the result.
        var actual = k.Result;

        var maxAbsDiff = 0.0f;
        for (var i = 0; i < actual.Length; i++)
            maxAbsDiff = Math.Max(maxAbsDiff, Math.Abs(actual[i] - expected[i]));

        // FP32 matmul over 64 adds can accumulate ~1e-4 rounding error per element.
        Assert.True(maxAbsDiff < 1e-3f, $"Max abs diff {maxAbsDiff} too large");
    }

    [Fact]
    public void GflopsFormulaIsCorrect()
    {
        var k = new SgemmKernel(n: 100);
        // For N=100, 1 second => 2 * 100^3 / 1 / 1e9 = 0.002 GFLOPS
        Assert.Equal(0.002, k.Gflops(1.0), precision: 6);
    }
}
