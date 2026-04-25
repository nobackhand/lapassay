using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;
using Lapassay.Core.Models;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// Dense single-precision matrix multiply C = A × B with A, B, C all N×N.
/// Uses System.Numerics.Vector&lt;float&gt; for SIMD (AVX2/AVX-512 via hardware-accelerated path)
/// and parallelizes over rows. Reports GFLOPS = 2 * N^3 / seconds / 1e9.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SgemmKernel
{
    readonly int _n;
    readonly float[] _a;
    readonly float[] _b;
    readonly float[] _c;

    public SgemmKernel(int n = 1024, int seed = 42)
    {
        _n = n;
        _a = new float[n * n];
        _b = new float[n * n];
        _c = new float[n * n];
        var rng = new Random(seed);
        for (var i = 0; i < _a.Length; i++) _a[i] = (float)(rng.NextDouble() * 2 - 1);
        for (var i = 0; i < _b.Length; i++) _b[i] = (float)(rng.NextDouble() * 2 - 1);
    }

    /// <summary>One benchmark iteration — full matmul.</summary>
    public void Run()
    {
        // Transpose B to B^T so inner loop walks contiguous floats in both A and B^T.
        // A[i,k] * B[k,j] == A[i,k] * BT[j,k]  — inner dot-product loop vectorizes cleanly.
        var bt = Transpose(_b, _n);
        var vectorSize = Vector<float>.Count;
        var n = _n;

        Parallel.For(0, n, i =>
        {
            var aRowStart = i * n;
            for (var j = 0; j < n; j++)
            {
                var btRowStart = j * n;
                var accVec = Vector<float>.Zero;
                var k = 0;
                for (; k <= n - vectorSize; k += vectorSize)
                {
                    var va = new Vector<float>(_a, aRowStart + k);
                    var vb = new Vector<float>(bt, btRowStart + k);
                    accVec += va * vb;
                }
                var acc = Vector.Dot(accVec, Vector<float>.One);
                for (; k < n; k++)
                {
                    acc += _a[aRowStart + k] * bt[btRowStart + k];
                }
                _c[aRowStart + j] = acc;
            }
        });

        // Prevent DCE.
        Sink.Consume(_c.AsSpan());
    }

    public double Gflops(double seconds) => 2.0 * _n * _n * _n / seconds / 1e9;

    public int N => _n;
    public float[] Result => _c;

    static float[] Transpose(float[] m, int n)
    {
        var t = new float[n * n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                t[j * n + i] = m[i * n + j];
        return t;
    }

    /// <summary>Compute a reference result via naive O(N^3) for correctness tests.</summary>
    public static float[] NaiveReference(float[] a, float[] b, int n)
    {
        var c = new float[n * n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                float acc = 0;
                for (var k = 0; k < n; k++)
                    acc += a[i * n + k] * b[k * n + j];
                c[i * n + j] = acc;
            }
        return c;
    }
}
