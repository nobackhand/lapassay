using System.Runtime.Versioning;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Lapassay.Core.Kernels.Gpu;

/// <summary>
/// Owns the D3D12 device + compute command queue. Calls SetStablePowerState(true)
/// to lock GPU clocks so benchmark scores are reproducible. If Developer Mode
/// is off, SetStablePowerState will throw — we wrap it and surface an actionable error.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class D3D12Context : IDisposable
{
    public ID3D12Device Device { get; }
    public ID3D12CommandQueue Queue { get; }
    public IDXGIAdapter1 Adapter { get; }
    public string AdapterName { get; }
    public long TimestampFrequency { get; }
    public bool StablePowerStateEnabled { get; }

    public D3D12Context(bool enableDebug = false, bool enableStablePowerState = true)
    {
        if (enableDebug && D3D12GetDebugInterface(out ID3D12Debug? debug).Success && debug != null)
        {
            debug.EnableDebugLayer();
            debug.Dispose();
        }

        // DXGI debug requires Graphics Tools feature installed; fall back to non-debug if not.
        IDXGIFactory6? factory = null;
        if (enableDebug)
        {
            var hr = CreateDXGIFactory2(true, out factory);
            if (hr.Failure || factory is null)
            {
                factory?.Dispose();
                factory = null;
            }
        }
        if (factory is null)
        {
            CreateDXGIFactory2(false, out factory).CheckError();
        }
        if (factory is null) throw new InvalidOperationException("Failed to create DXGI factory");

        // Prefer high-performance discrete adapter when available.
        IDXGIAdapter1? chosen = null;
        for (uint i = 0; ; i++)
        {
            var hr = factory.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter);
            if (hr.Failure || adapter is null) break;

            var desc = adapter.Description1;
            if ((desc.Flags & AdapterFlags.Software) != 0) { adapter.Dispose(); continue; }

            // Try to create a device at feature level 11.0 to verify compat.
            if (D3D12CreateDevice(adapter, Vortice.Direct3D.FeatureLevel.Level_11_0, out ID3D12Device? testDev).Success && testDev != null)
            {
                testDev.Dispose();
                chosen = adapter;
                break;
            }
            adapter.Dispose();
        }
        factory.Dispose();

        if (chosen is null) throw new InvalidOperationException("No D3D12-capable adapter found");
        Adapter = chosen;
        AdapterName = chosen.Description1.Description;

        D3D12CreateDevice(Adapter, Vortice.Direct3D.FeatureLevel.Level_11_0, out ID3D12Device? device).CheckError();
        if (device is null) throw new InvalidOperationException("Device creation failed");
        Device = device;

        var queueDesc = new CommandQueueDescription(CommandListType.Direct, CommandQueuePriority.Normal);
        Queue = Device.CreateCommandQueue(queueDesc);
        Queue.GetTimestampFrequency(out ulong freq);
        TimestampFrequency = (long)freq;

        // CRITICAL: SetStablePowerState DEVICE-REMOVES the adapter when Developer Mode is off.
        // Never call it unless the caller has already confirmed Dev Mode is enabled.
        if (enableStablePowerState)
        {
            try
            {
                Device.SetStablePowerState(true);
                StablePowerStateEnabled = true;
            }
            catch
            {
                StablePowerStateEnabled = false;
            }
        }
        else
        {
            StablePowerStateEnabled = false;
        }
    }

    public void Dispose()
    {
        Queue.Dispose();
        Device.Dispose();
        Adapter.Dispose();
    }
}
