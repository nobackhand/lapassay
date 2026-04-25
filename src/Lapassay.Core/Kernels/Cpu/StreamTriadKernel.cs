using System.Numerics;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// McCalpin STREAM Triad: A[i] = B[i] + s * C[i].
/// Arrays sized well beyond L3 cache (32 MB each) so we measure sustainable
/// DRAM bandwidth, not cache bandwidth. Parallelized across cores with SIMD
/// inner loop. Reports GB/s of effective memory bandwidth.
///
/// Bandwidth accounting: per iteration we read B and C, write A — 3 arrays
/// touched, each element 8 bytes (double). Bytes transferred = 3 * N * 8.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StreamTriadKernel
{
    const int ArrayBytes = 32 * 1024 * 1024; // 32 MB per array, >>L3 (8 MB on target)
    const int N = ArrayBytes / sizeof(double);

    readonly double[] _a;
    readonly double[] _b;
    readonly double[] _c;
    const double Scalar = 3.0;

    public StreamTriadKernel()
    {
        _a = new double[N];
        _b = new double[N];
        _c = new double[N];
        for (var i = 0; i < N; i++)
        {
            _b[i] = 1.0 + i * 1e-6;
            _c[i] = 2.0 + i * 1e-6;
        }
    }

    public void Run()
    {
        var vecSize = Vector<double>.Count;
        var scalar = new Vector<double>(Scalar);
        var n = N;

        Parallel.For(0, Environment.ProcessorCount, tid =>
        {
            var chunk = n / Environment.ProcessorCount;
            var start = tid * chunk;
            var end = tid == Environment.ProcessorCount - 1 ? n : start + chunk;

            var i = start;
            for (; i <= end - vecSize; i += vecSize)
            {
                var vb = new Vector<double>(_b, i);
                var vc = new Vector<double>(_c, i);
                var va = vb + scalar * vc;
                va.CopyTo(_a, i);
            }
            for (; i < end; i++) _a[i] = _b[i] + Scalar * _c[i];
        });

        Sink.Consume(_a[0]);
    }

    public double GbPerSecond(double seconds)
    {
        var bytes = 3.0 * N * sizeof(double);
        return bytes / seconds / 1e9;
    }
}
