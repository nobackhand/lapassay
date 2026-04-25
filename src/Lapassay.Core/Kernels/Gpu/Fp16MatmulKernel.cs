using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Lapassay.Core.Kernels.Gpu;

/// <summary>
/// FP16 matmul: 2048×2048 compute with FP16 inputs (`min16float`) and FP32 output.
/// A and B are packed FP16 (2 bytes/element) in byte-address buffers; C is FP32.
/// On hardware with FP16 ALU packed-math support (RDNA2+, Intel Xe, NVIDIA Turing+),
/// this delivers roughly 2× the FP32 throughput.
///
/// Output stays FP32 so each output element is 4-byte aligned — avoids inter-thread
/// race conditions when writing adjacent FP16 outputs into shared 32-bit words.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Fp16MatmulKernel : IDisposable
{
    const string ShaderSource = @"
cbuffer Params : register(b0) {
    uint N;
    uint3 _pad;
};

RWByteAddressBuffer A : register(u0);  // FP16 input
RWByteAddressBuffer B : register(u1);  // FP16 input
RWByteAddressBuffer C : register(u2);  // FP32 output

#define TILE 8

// Unpack one FP16 value from an RWByteAddressBuffer at half-index `idx`.
min16float LoadHalf(RWByteAddressBuffer buf, uint idx) {
    uint byteIdx = idx * 2;
    uint aligned = byteIdx & ~3u;
    uint word = buf.Load(aligned);
    uint shift = (byteIdx & 2) * 8;
    return min16float(f16tof32((word >> shift) & 0xFFFF));
}

[numthreads(TILE, TILE, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID) {
    uint row = tid.y;
    uint col = tid.x;
    if (row >= N || col >= N) return;

    min16float acc = (min16float)0;
    [loop] for (uint k = 0; k < N; ++k) {
        acc += LoadHalf(A, row * N + k) * LoadHalf(B, k * N + col);
    }
    C.Store((row * N + col) * 4, asuint(float(acc)));
}";

    readonly D3D12Context _ctx;
    readonly int _n;
    readonly int _iterations;

    readonly ID3D12RootSignature _rootSig;
    readonly ID3D12PipelineState _pso;
    readonly ID3D12Resource _bufferA;  // FP16
    readonly ID3D12Resource _bufferB;  // FP16
    readonly ID3D12Resource _bufferC;  // FP32
    readonly ID3D12QueryHeap _queryHeap;
    readonly ID3D12Resource _readbackBuffer;
    readonly ID3D12CommandAllocator _allocator;
    readonly ID3D12GraphicsCommandList _cmdList;
    readonly ID3D12Fence _fence;
    readonly AutoResetEvent _fenceEvent = new(false);
    ulong _fenceValue;

    public Fp16MatmulKernel(D3D12Context ctx, int n = 2048, int measurementIterations = 10)
    {
        _ctx = ctx;
        _n = n;
        _iterations = measurementIterations;

        _rootSig = CreateRootSignature();
        _pso = CreatePso();

        var halfBytes = (ulong)(n * n * sizeof(ushort));  // FP16 = 2 bytes
        var floatBytes = (ulong)(n * n * sizeof(float));  // FP32 = 4 bytes
        _bufferA = CreateDefaultBuffer(halfBytes);
        _bufferB = CreateDefaultBuffer(halfBytes);
        _bufferC = CreateDefaultBuffer(floatBytes);
        InitInputBuffers(halfBytes);

        _queryHeap = ctx.Device.CreateQueryHeap<ID3D12QueryHeap>(
            new QueryHeapDescription(QueryHeapType.Timestamp, (uint)(_iterations * 2)));

        var rbDesc = ResourceDescription.Buffer((ulong)(_iterations * 2 * sizeof(ulong)));
        _readbackBuffer = ctx.Device.CreateCommittedResource(
            HeapProperties.ReadbackHeapProperties,
            HeapFlags.None,
            rbDesc,
            ResourceStates.CopyDest);

        _allocator = ctx.Device.CreateCommandAllocator(CommandListType.Direct);
        _cmdList = ctx.Device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _allocator, null);
        _cmdList.Close();

        _fence = ctx.Device.CreateFence(0);
    }

    ID3D12RootSignature CreateRootSignature()
    {
        var parameters = new RootParameter1[]
        {
            new RootParameter1(new RootConstants(0, 0, 4), ShaderVisibility.All),
            new RootParameter1(RootParameterType.UnorderedAccessView,
                new RootDescriptor1(0, 0, RootDescriptorFlags.DataVolatile), ShaderVisibility.All),
            new RootParameter1(RootParameterType.UnorderedAccessView,
                new RootDescriptor1(1, 0, RootDescriptorFlags.DataVolatile), ShaderVisibility.All),
            new RootParameter1(RootParameterType.UnorderedAccessView,
                new RootDescriptor1(2, 0, RootDescriptorFlags.DataVolatile), ShaderVisibility.All),
        };
        var desc = new RootSignatureDescription1(RootSignatureFlags.None, parameters, Array.Empty<StaticSamplerDescription>());
        var versioned = new VersionedRootSignatureDescription(desc);

        var err = D3D12.D3D12SerializeVersionedRootSignature(versioned, out Blob? blob);
        if (!string.IsNullOrEmpty(err) || blob is null)
        {
            blob?.Dispose();
            throw new InvalidOperationException($"Root signature serialize failed: {err}");
        }
        var rs = _ctx.Device.CreateRootSignature(0, blob);
        blob.Dispose();
        return rs;
    }

    ID3D12PipelineState CreatePso()
    {
        // cs_5_1 is needed for min16float support.
        var hr = Compiler.Compile(ShaderSource, "CSMain", "matmul_fp16.hlsl", "cs_5_1",
            out Blob? blob, out Blob? errors);
        if (hr.Failure || blob is null)
        {
            var msg = errors is not null
                ? System.Text.Encoding.ASCII.GetString(errors.AsSpan())
                : hr.Description;
            errors?.Dispose();
            throw new InvalidOperationException($"FP16 shader compile failed: {msg}");
        }
        errors?.Dispose();

        var shaderBytes = blob.AsBytes();
        blob.Dispose();

        var psoDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSig,
            ComputeShader = shaderBytes,
        };
        return _ctx.Device.CreateComputePipelineState(psoDesc);
    }

    ID3D12Resource CreateDefaultBuffer(ulong sizeBytes)
    {
        var desc = ResourceDescription.Buffer(sizeBytes, ResourceFlags.AllowUnorderedAccess);
        return _ctx.Device.CreateCommittedResource(
            HeapProperties.DefaultHeapProperties,
            HeapFlags.None,
            desc,
            ResourceStates.UnorderedAccess);
    }

    void InitInputBuffers(ulong halfBytes)
    {
        var uploadDesc = ResourceDescription.Buffer(halfBytes);
        var uploadA = _ctx.Device.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None, uploadDesc, ResourceStates.GenericRead);
        var uploadB = _ctx.Device.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None, uploadDesc, ResourceStates.GenericRead);

        var count = (int)(halfBytes / sizeof(ushort));
        var halfData = new ushort[count];
        var rng = new Random(42);
        for (var i = 0; i < count; i++)
            halfData[i] = BitConverter.HalfToUInt16Bits((Half)(float)(rng.NextDouble() * 2 - 1));

        WriteBytes(uploadA, (int)halfBytes, MemoryMarshal.AsBytes(halfData.AsSpan()));
        for (var i = 0; i < count; i++)
            halfData[i] = BitConverter.HalfToUInt16Bits((Half)(float)(rng.NextDouble() * 2 - 1));
        WriteBytes(uploadB, (int)halfBytes, MemoryMarshal.AsBytes(halfData.AsSpan()));

        var allocator = _ctx.Device.CreateCommandAllocator(CommandListType.Direct);
        var cl = _ctx.Device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, allocator, null);

        cl.ResourceBarrierTransition(_bufferA, ResourceStates.UnorderedAccess, ResourceStates.CopyDest);
        cl.ResourceBarrierTransition(_bufferB, ResourceStates.UnorderedAccess, ResourceStates.CopyDest);
        cl.CopyBufferRegion(_bufferA, 0, uploadA, 0, halfBytes);
        cl.CopyBufferRegion(_bufferB, 0, uploadB, 0, halfBytes);
        cl.ResourceBarrierTransition(_bufferA, ResourceStates.CopyDest, ResourceStates.UnorderedAccess);
        cl.ResourceBarrierTransition(_bufferB, ResourceStates.CopyDest, ResourceStates.UnorderedAccess);
        cl.Close();
        _ctx.Queue.ExecuteCommandList(cl);

        var copyFence = _ctx.Device.CreateFence(0);
        _ctx.Queue.Signal(copyFence, 1).CheckError();
        if (copyFence.CompletedValue < 1)
        {
            using var evt = new AutoResetEvent(false);
            copyFence.SetEventOnCompletion(1, evt).CheckError();
            evt.WaitOne();
        }

        cl.Dispose();
        allocator.Dispose();
        uploadA.Dispose();
        uploadB.Dispose();
        copyFence.Dispose();
    }

    static void WriteBytes(ID3D12Resource buffer, int length, ReadOnlySpan<byte> bytes)
    {
        var dst = buffer.Map<byte>(0, length);
        bytes.CopyTo(dst);
        buffer.Unmap(0);
    }

    public double[] Measure(int warmupIterations = 5)
    {
        Dispatch(warmupIterations, timed: false);
        return Dispatch(_iterations, timed: true);
    }

    double[] Dispatch(int iterations, bool timed)
    {
        _allocator.Reset();
        _cmdList.Reset(_allocator, _pso);

        _cmdList.SetPipelineState(_pso);
        _cmdList.SetComputeRootSignature(_rootSig);
        _cmdList.SetComputeRoot32BitConstant(0, (uint)_n, 0);
        _cmdList.SetComputeRootUnorderedAccessView(1, _bufferA.GPUVirtualAddress);
        _cmdList.SetComputeRootUnorderedAccessView(2, _bufferB.GPUVirtualAddress);
        _cmdList.SetComputeRootUnorderedAccessView(3, _bufferC.GPUVirtualAddress);

        const int TILE = 8;
        var groups = (uint)((_n + TILE - 1) / TILE);

        for (var i = 0; i < iterations; i++)
        {
            if (timed) _cmdList.EndQuery(_queryHeap, QueryType.Timestamp, (uint)(i * 2));
            _cmdList.Dispatch(groups, groups, 1);
            if (timed) _cmdList.EndQuery(_queryHeap, QueryType.Timestamp, (uint)(i * 2 + 1));
            _cmdList.ResourceBarrierUnorderedAccessView(_bufferC);
        }

        if (timed)
            _cmdList.ResolveQueryData(_queryHeap, QueryType.Timestamp, 0, (uint)(iterations * 2),
                _readbackBuffer, 0);

        _cmdList.Close();
        _ctx.Queue.ExecuteCommandList(_cmdList);

        _fenceValue++;
        _ctx.Queue.Signal(_fence, _fenceValue).CheckError();
        if (_fence.CompletedValue < _fenceValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent).CheckError();
            _fenceEvent.WaitOne();
        }

        if (!timed) return Array.Empty<double>();

        var bytesNeeded = iterations * 2 * sizeof(ulong);
        var src = _readbackBuffer.Map<byte>(0, bytesNeeded);
        var timestamps = MemoryMarshal.Cast<byte, ulong>(src).Slice(0, iterations * 2).ToArray();
        _readbackBuffer.Unmap(0);

        var hz = (double)_ctx.TimestampFrequency;
        var valid = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var dtTicks = timestamps[i * 2 + 1] - timestamps[i * 2];
            if (dtTicks == 0) continue;
            var seconds = dtTicks / hz;
            if (seconds < 1e-4) continue;
            valid.Add(seconds);
        }
        if (valid.Count == 0)
            throw new InvalidOperationException("All GPU FP16 timestamps invalid; try again.");
        return valid.ToArray();
    }

    public double Gflops(double seconds) => 2.0 * _n * _n * _n / seconds / 1e9;
    public int N => _n;

    public void Dispose()
    {
        _fenceEvent.Dispose();
        _fence.Dispose();
        _cmdList.Dispose();
        _allocator.Dispose();
        _readbackBuffer.Dispose();
        _queryHeap.Dispose();
        _bufferC.Dispose();
        _bufferB.Dispose();
        _bufferA.Dispose();
        _pso.Dispose();
        _rootSig.Dispose();
    }
}
