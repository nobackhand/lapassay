namespace Lapassay.Core;

/// <summary>
/// One-line plain-English descriptions for every benchmark id we ship.
/// Kept here (in Core) so the CLI, GUI, and HTML report can all show the
/// same labels without duplicating strings.
/// </summary>
public static class BenchmarkCatalog
{
    static readonly Dictionary<string, string> ShortDescriptions = new()
    {
        // CPU integer / crypto
        ["cpu.aes128cbc"]            = "AES-128 encryption throughput",
        ["cpu.sha256"]               = "SHA-256 hash throughput",
        ["cpu.zstd.level3"]          = "Zstd level-3 compression throughput",

        // CPU float / SIMD
        ["cpu.sgemm.fp32.1024"]      = "1024² FP32 matrix multiply (SIMD, multi-core)",
        ["cpu.fft.c2c.4096"]         = "4096-point complex FFT",
        ["cpu.mandelbrot.2048"]      = "2048² Mandelbrot fractal (SIMD double)",

        // CPU memory
        ["cpu.stream.triad"]         = "STREAM Triad sustained memory bandwidth",
        ["cpu.latency.pointerchase"] = "Random pointer-chase memory latency (lower is better)",

        // CPU parallel
        ["cpu.scaling.efficiency"]   = "Multi-core scaling efficiency at full cores",

        // GPU compute
        ["gpu.matmul.fp32.2048"]     = "2048² GPU matrix multiply (FP32, D3D12 compute)",
        ["gpu.matmul.fp16.2048"]     = "2048² GPU matrix multiply (FP16 / packed half)",

        // GPU AI
        ["gpu.ai.squeezenet"]        = "SqueezeNet image inference (ONNX + DirectML)",
    };

    public static string Describe(string id) =>
        ShortDescriptions.TryGetValue(id, out var d) ? d : id;
}
