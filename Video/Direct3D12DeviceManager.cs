using System.Runtime.InteropServices;

namespace PodcastWorkbench.Video;

internal sealed class Direct3D12DeviceManager : IDisposable
{
    private const int D3D_FEATURE_LEVEL_12_0 = 0xc000;
    private static readonly Guid ID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    private IntPtr _device;
    private IMFDXGIDeviceManager? _manager;

    private Direct3D12DeviceManager(IntPtr device, IMFDXGIDeviceManager manager)
    {
        _device = device;
        _manager = manager;
    }

    public IMFDXGIDeviceManager Manager => _manager
        ?? throw new ObjectDisposedException(nameof(Direct3D12DeviceManager));

    public static Direct3D12DeviceManager Create()
    {
        MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.D3D12CreateDevice(
            IntPtr.Zero,
            D3D_FEATURE_LEVEL_12_0,
            ID3D12Device,
            out var device));

        try
        {
            MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateDXGIDeviceManager(
                out var resetToken,
                out var manager));
            MediaFoundationInterop.ThrowIfFailed(manager.ResetDevice(device, resetToken));
            return new Direct3D12DeviceManager(device, manager);
        }
        catch
        {
            if (device != IntPtr.Zero)
            {
                Marshal.Release(device);
            }

            throw;
        }
    }

    public void Dispose()
    {
        MediaFoundationInterop.ReleaseComObject(_manager);
        _manager = null;

        if (_device != IntPtr.Zero)
        {
            Marshal.Release(_device);
            _device = IntPtr.Zero;
        }
    }
}
