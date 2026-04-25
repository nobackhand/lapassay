using System.Numerics;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// 1D Cooley-Tukey radix-2 complex FFT, n=4096, performed in-place.
/// Reports MFLOP/s using the conventional 5*n*log2(n) op count for a complex FFT.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FftKernel
{
    readonly int _n;
    readonly int _log2n;
    readonly Complex[] _data;
    readonly Complex[] _work;

    public FftKernel(int n = 4096)
    {
        if ((n & (n - 1)) != 0) throw new ArgumentException("N must be a power of two", nameof(n));
        _n = n;
        _log2n = BitOperations.Log2((uint)n);
        _data = new Complex[n];
        _work = new Complex[n];

        // Signal: sum of a few cosines + noise — a realistic FFT input.
        var rng = new Random(42);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            _data[i] = new Complex(
                Math.Cos(2 * Math.PI * 5 * t) + 0.5 * Math.Cos(2 * Math.PI * 17 * t) + 0.1 * (rng.NextDouble() - 0.5),
                0);
        }
    }

    public void Run()
    {
        Array.Copy(_data, _work, _n);
        Fft(_work, _n, _log2n);
        Sink.Consume(_work[0].Real);
    }

    static void Fft(Complex[] x, int n, int log2n)
    {
        // Bit-reverse permutation
        for (var i = 0; i < n; i++)
        {
            var j = BitReverse(i, log2n);
            if (j > i) (x[i], x[j]) = (x[j], x[i]);
        }

        // Butterflies
        for (var s = 1; s <= log2n; s++)
        {
            var m = 1 << s;
            var mHalf = m >> 1;
            var omega = Complex.Exp(new Complex(0, -2 * Math.PI / m));
            for (var k = 0; k < n; k += m)
            {
                var w = Complex.One;
                for (var j = 0; j < mHalf; j++)
                {
                    var t = w * x[k + j + mHalf];
                    var u = x[k + j];
                    x[k + j] = u + t;
                    x[k + j + mHalf] = u - t;
                    w *= omega;
                }
            }
        }
    }

    static int BitReverse(int x, int bits)
    {
        var result = 0;
        for (var i = 0; i < bits; i++)
        {
            result = (result << 1) | (x & 1);
            x >>= 1;
        }
        return result;
    }

    public double Mflops(double seconds)
    {
        // Standard count: 5 * N * log2(N) for a complex FFT (complex mul = 6 flops, add = 2; avg ~5*N*log2N).
        var ops = 5.0 * _n * _log2n;
        return ops / seconds / 1e6;
    }

    public int N => _n;
    public Complex[] Result => _work;
    public Complex[] Input => _data;
}
