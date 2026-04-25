using System.Runtime.Versioning;
using System.Security.Cryptography;
using Lapassay.Core.Harness;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// AES-128-CBC encryption throughput over a 16 MB buffer.
/// .NET's <see cref="Aes"/> uses AES-NI on supported hardware (Intel/AMD x86-64
/// with AES instruction set). Reports throughput in MB/s.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AesKernel : IDisposable
{
    const int BufferSize = 16 * 1024 * 1024; // 16 MB
    readonly byte[] _plaintext;
    readonly byte[] _ciphertext;
    readonly Aes _aes;
    readonly ICryptoTransform _enc;

    public AesKernel()
    {
        _plaintext = new byte[BufferSize];
        _ciphertext = new byte[BufferSize + 32]; // room for PKCS7 padding
        new Random(42).NextBytes(_plaintext);
        _aes = Aes.Create();
        _aes.KeySize = 128;
        _aes.Mode = CipherMode.CBC;
        _aes.Padding = PaddingMode.None; // fixed block-sized input => no padding
        _aes.GenerateKey();
        _aes.GenerateIV();
        _enc = _aes.CreateEncryptor();
    }

    public void Run()
    {
        // Encrypts entire buffer in one TransformFinalBlock. For CBC, AES-NI is used if available.
        var n = _enc.TransformBlock(_plaintext, 0, BufferSize, _ciphertext, 0);
        Sink.Consume(n);
    }

    public double MegabytesPerSecond(double seconds) => BufferSize / (1024.0 * 1024.0) / seconds;
    public int BytesProcessed => BufferSize;

    public void Dispose()
    {
        _enc.Dispose();
        _aes.Dispose();
    }
}
