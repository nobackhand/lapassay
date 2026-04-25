using System.Runtime.Versioning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lapassay.Core.Kernels.Gpu;

/// <summary>
/// Single-image CNN inference benchmark via ONNX Runtime with the DirectML execution
/// provider. DirectML runs on any D3D12-capable GPU/NPU — NVIDIA, AMD, Intel, Qualcomm
/// alike — so this kernel gives a hardware-agnostic "AI compute" score.
///
/// Model: SqueezeNet 1.0 (opset 9), input shape 1×3×224×224 FP32, output 1000 ImageNet logits.
/// About 1.24 GFLOP per inference — small and fast (~1-5 ms on typical laptop GPUs).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OnnxInferenceKernel : IDisposable
{
    readonly SessionOptions _options;
    readonly InferenceSession _session;
    readonly string _inputName;
    readonly IReadOnlyCollection<NamedOnnxValue> _inputs;

    public OnnxInferenceKernel(string? modelPath = null, int deviceId = 0)
    {
        modelPath ??= FindModel();
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"SqueezeNet model not found at {modelPath}. " +
                "Expected copied to output dir via the csproj <None Include=...> entry.");

        _options = new SessionOptions();
        // Append DirectML EP for GPU acceleration on any D3D12 adapter.
        _options.AppendExecutionProvider_DML(deviceId);
        // Disable memory-pattern optimization — required for DirectML (docs).
        _options.EnableMemoryPattern = false;
        _options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

        _session = new InferenceSession(modelPath, _options);
        _inputName = _session.InputMetadata.Keys.First();

        // SqueezeNet wants 1×3×224×224 FP32. Fill with zeros — timing only cares about shape,
        // not content. (A real classifier would need a normalized image here.)
        var tensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
        _inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
    }

    static string FindModel()
    {
        // When built, the csproj copies the model to `<output>\models\squeezenet1.0-9.onnx`.
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "models", "squeezenet1.0-9.onnx");
        if (File.Exists(fromOutput)) return fromOutput;

        // Running from source tree without copy — fall back to repo asset.
        var cwd = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(cwd, "assets", "models", "squeezenet1.0-9.onnx");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(cwd);
            if (parent is null) break;
            cwd = parent.FullName;
        }

        return fromOutput; // returns the expected path; caller throws if missing
    }

    public void Run()
    {
        using var results = _session.Run(_inputs);
    }

    public double InferencesPerSecond(double seconds) => 1.0 / seconds;

    public void Dispose()
    {
        _session.Dispose();
        _options.Dispose();
    }
}
