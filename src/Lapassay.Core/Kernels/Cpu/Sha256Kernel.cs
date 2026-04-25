using System.Runtime.Versioning;
using System.Security.Cryptography;
using Lapassay.Core.Harness;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// SHA-256 hashing throughput over a 16 MB buffer.
/// .NET's <see cref="SHA256"/> uses SHA-NI instructions on supported hardware
/// (Intel Goldmont+, AMD Zen+). Reports throughput in MB/s.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Sha256Kernel
{
    const int BufferSize = 16 * 1024 * 1024; // 16 MB
    readonly byte[] _data;
    readonly byte[] _hash = new byte[32];

    public Sha256Kernel()
    {
        _data = new byte[BufferSize];
        new Random(42).NextBytes(_data);
    }

    public void Run()
    {
        SHA256.HashData(_data, _hash);
        Sink.Consume(_hash[0]);
    }

    public double MegabytesPerSecond(double seconds) => BufferSize / (1024.0 * 1024.0) / seconds;
    public int BytesProcessed => BufferSize;
}
