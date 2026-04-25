using System.Runtime.Versioning;
using Lapassay.Core.Harness;
using ZstdSharp;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// Zstd compression throughput at level 3 over a 4 MB semi-compressible buffer.
/// Data is a mix of random bytes and structured runs so there's real work to do.
/// Reports throughput in MB/s of *input* bytes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ZstdKernel : IDisposable
{
    const int InputSize = 4 * 1024 * 1024; // 4 MB
    readonly byte[] _input;
    readonly byte[] _output;
    readonly Compressor _compressor;

    public ZstdKernel()
    {
        _input = new byte[InputSize];
        BuildSemiCompressible(_input);
        _output = new byte[Compressor.GetCompressBound(InputSize)];
        _compressor = new Compressor(3);
    }

    static void BuildSemiCompressible(byte[] buf)
    {
        var rng = new Random(42);
        var i = 0;
        while (i < buf.Length)
        {
            if (rng.NextDouble() < 0.5)
            {
                // Random run
                var n = Math.Min(rng.Next(4, 128), buf.Length - i);
                for (var j = 0; j < n; j++) buf[i + j] = (byte)rng.Next(256);
                i += n;
            }
            else
            {
                // Repeated byte run
                var n = Math.Min(rng.Next(8, 512), buf.Length - i);
                var b = (byte)rng.Next(256);
                for (var j = 0; j < n; j++) buf[i + j] = b;
                i += n;
            }
        }
    }

    public void Run()
    {
        var written = _compressor.Wrap(_input, _output, 0);
        Sink.Consume(written);
    }

    public double MegabytesPerSecond(double seconds) => InputSize / (1024.0 * 1024.0) / seconds;
    public int BytesProcessed => InputSize;

    public void Dispose() => _compressor.Dispose();
}
