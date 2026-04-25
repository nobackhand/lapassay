using System.Runtime.Versioning;
using Lapassay.Core.Harness;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// Cache-line pointer chase: linked list of indices shuffled randomly, with
/// stride = 64 bytes (typical cache line). Single-threaded — this measures
/// memory latency, not bandwidth. The dependency chain (each load depends on
/// the previous) prevents the CPU from prefetching ahead.
///
/// Working set: 32 MB (much larger than L3). Reports nanoseconds per access.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PointerChaseKernel
{
    const int BufferBytes = 32 * 1024 * 1024; // 32 MB >>L3
    const int Stride = 64;                     // cache-line stride
    const int NNodes = BufferBytes / Stride;
    const int TraversalCount = 1_000_000;

    readonly int[] _next;

    public PointerChaseKernel()
    {
        // Each "node" occupies 64 bytes; we only use the first int (the "next" pointer).
        // Represent as int[] where _next[i * 16] holds the index of the next node.
        _next = new int[BufferBytes / sizeof(int)];

        // Random permutation of node indices.
        var order = Enumerable.Range(0, NNodes).ToArray();
        var rng = new Random(42);
        for (var i = NNodes - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // Chain nodes: order[0] -> order[1] -> ... -> order[N-1] -> order[0]
        for (var i = 0; i < NNodes; i++)
        {
            var from = order[i] * (Stride / sizeof(int));
            var to = order[(i + 1) % NNodes] * (Stride / sizeof(int));
            _next[from] = to;
        }
    }

    public void Run()
    {
        var idx = 0;
        // Force dependency chain: each iteration reads index-dependent-on-previous.
        for (var i = 0; i < TraversalCount; i++)
        {
            idx = _next[idx];
        }
        Sink.Consume(idx);
    }

    public double NanosecondsPerAccess(double seconds) => seconds / TraversalCount * 1e9;
    public int Accesses => TraversalCount;
}
