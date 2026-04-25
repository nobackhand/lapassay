using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;
using Lapassay.Core.Models;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// Per-core scaling test. Runs the same SGEMM workload at increasing thread
/// counts and reports the resulting GFLOPS curve plus a single "scaling
/// efficiency" score (= GFLOPS at full cores ÷ (single-thread GFLOPS × cores)).
///
/// Reveals:
///   * heterogeneous-core asymmetry (P-cores vs E-cores)
///   * memory-bandwidth ceilings on iGPU laptops
///   * thermal throttling that only shows up under sustained multi-thread load
///   * OS scheduler placement quality
///
/// 100% efficiency means perfect linear scaling; real laptop CPUs typically
/// land in the 60–85% range. Anything under 50% suggests the system is
/// memory-bound or already throttling.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CpuScalingKernel
{
    readonly int _n;
    readonly float[] _a;
    readonly float[] _bt; // pre-transposed B for cache-friendly inner loop
    readonly float[] _c;
    readonly int[] _threadCounts;

    public CpuScalingKernel(int n, int physicalCores)
    {
        _n = n;
        _a = new float[n * n];
        _bt = new float[n * n];
        _c = new float[n * n];

        var rng = new Random(7);
        for (var i = 0; i < _a.Length; i++) _a[i] = (float)(rng.NextDouble() * 2 - 1);
        var b = new float[n * n];
        for (var i = 0; i < b.Length; i++) b[i] = (float)(rng.NextDouble() * 2 - 1);
        // Transpose B once so inner loop walks contiguously in both A and B^T.
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                _bt[j * n + i] = b[i * n + j];

        // Sweep: 1, 2, 4, 8, ..., capped at physical_cores. Always include physical_cores.
        var counts = new List<int>();
        var k = 1;
        while (k < physicalCores) { counts.Add(k); k *= 2; }
        if (counts.Count == 0 || counts[^1] != physicalCores) counts.Add(physicalCores);
        _threadCounts = counts.ToArray();
    }

    public List<ScalingPoint> RunSweep(int warmupIterations = 1, int measurementIterations = 3)
    {
        // Warm up at full thread count to reach steady-state JIT + cache.
        for (var w = 0; w < warmupIterations; w++) RunWithThreads(_threadCounts[^1]);

        var times1 = new List<double>();
        var results = new List<(int threads, double gflops)>();

        foreach (var threads in _threadCounts)
        {
            // Brief warmup so the OS doesn't park cores between sweeps.
            RunWithThreads(threads);

            var samples = new double[measurementIterations];
            for (var i = 0; i < measurementIterations; i++)
            {
                var sw = Stopwatch.StartNew();
                RunWithThreads(threads);
                sw.Stop();
                samples[i] = sw.Elapsed.TotalSeconds;
            }
            Array.Sort(samples);
            var median = samples[measurementIterations / 2];
            var gflops = 2.0 * _n * _n * _n / median / 1e9;
            results.Add((threads, gflops));
            if (threads == 1) times1.Add(median);
        }

        var single = results.First(r => r.threads == 1).gflops;
        var points = new List<ScalingPoint>();
        foreach (var (threads, gflops) in results)
        {
            var ideal = single * threads;
            var eff = ideal > 0 ? gflops / ideal * 100.0 : 0.0;
            points.Add(new ScalingPoint(threads, gflops, eff));
        }
        return points;
    }

    void RunWithThreads(int threads)
    {
        var n = _n;
        var vecSize = Vector<float>.Count;
        var opts = new ParallelOptions { MaxDegreeOfParallelism = threads };

        if (threads == 1)
        {
            // Single-thread path — avoid Parallel.For overhead entirely.
            for (var i = 0; i < n; i++)
                ComputeRow(i, n, vecSize);
        }
        else
        {
            Parallel.For(0, n, opts, i => ComputeRow(i, n, vecSize));
        }
        Sink.Consume(_c.AsSpan());
    }

    void ComputeRow(int i, int n, int vecSize)
    {
        var aRow = i * n;
        for (var j = 0; j < n; j++)
        {
            var bRow = j * n;
            var accVec = Vector<float>.Zero;
            var k = 0;
            for (; k <= n - vecSize; k += vecSize)
            {
                var va = new Vector<float>(_a, aRow + k);
                var vb = new Vector<float>(_bt, bRow + k);
                accVec += va * vb;
            }
            var acc = Vector.Dot(accVec, Vector<float>.One);
            for (; k < n; k++)
                acc += _a[aRow + k] * _bt[bRow + k];
            _c[aRow + j] = acc;
        }
    }

    /// <summary>Scaling efficiency at full cores (0..100%). Primary score-able metric.</summary>
    public static double EfficiencyAtFull(IReadOnlyList<ScalingPoint> points)
    {
        if (points.Count == 0) return 0;
        return points[^1].EfficiencyPct;
    }
}
