using System.Runtime.InteropServices;
using JerichoDown.Modules.Webcam.MediaFoundation;
using Vortice.DXGI;

namespace JerichoDown.Modules.Webcam.Dx11Bridge;

internal sealed class Direct3D11SharedTextureBridge : IDisposable
{
    private const int CreateTexture2DSlot = 5;
    private const int CopyResourceSlot = 47;
    private const int FlushSlot = 111;
    private const int D3D11UsageDefault = 0;
    private const int D3D11BindShaderResource = 0x8;
    private const int D3D11ResourceMiscSharedKeyedMutex = 0x100;
    private const int D3D11ResourceMiscSharedNtHandle = 0x800;
    private const int DxgiFormatNv12 = 103;
    private const int DuplicateSameAccess = 0x2;

    // DXGI keyed-mutex protocol: the mutex starts in state 0. The D3D11 producer acquires state 0,
    // writes the frame, then releases into state 1; the D3D12 consumer (Direct3D12PreviewHost)
    // acquires state 1, samples the frame, then releases back into state 0. This prevents the two
    // GPU contexts from touching the shared NV12 texture at the same time.
    private const ulong ProducerAcquireKey = 0;
    private const ulong ProducerReleaseKey = 1;

    // 0ms = a non-blocking test, not a wait: capture and render run at their own paces (not in
    // lockstep), so if the consumer hasn't finished with the texture yet, this frame is skipped
    // (TryCopyToSharedHandle already returns false/failureReason for that, which is tolerated) -
    // never stalling the capture thread. A blocking timeout here previously stalled capture for
    // up to its full duration on every frame the renderer fell behind on, which cascaded into
    // severe CPU/GPU load under normal (non-lockstep) operation.
    private const int KeyedMutexTimeoutMilliseconds = 0;

    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private readonly CreateTexture2DDelegate _createTexture2D;
    private readonly CopyResourceDelegate _copyResource;
    private readonly FlushDelegate _flush;
    private IntPtr _sharedTexture;
    private IntPtr _sharedHandle;
    private IDXGIKeyedMutex? _keyedMutex;
    private bool _disposed;

    public Direct3D11SharedTextureBridge(IntPtr device, IntPtr context, int width, int height)
    {
        if (device == IntPtr.Zero)
        {
            throw new ArgumentException("D3D11 device is missing.", nameof(device));
        }

        if (context == IntPtr.Zero)
        {
            throw new ArgumentException("D3D11 context is missing.", nameof(context));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Shared texture dimensions must be positive.");
        }

        _device = device;
        _context = context;
        Marshal.AddRef(_device);
        Marshal.AddRef(_context);
        _createTexture2D = GetComMethod<CreateTexture2DDelegate>(_device, CreateTexture2DSlot);
        _copyResource = GetComMethod<CopyResourceDelegate>(_context, CopyResourceSlot);
        _flush = GetComMethod<FlushDelegate>(_context, FlushSlot);
        CreateSharedTexture(width, height);
    }

    public bool TryCopyToSharedHandle(IntPtr sourceTexture, out IntPtr duplicatedSharedHandle, out string? failureReason)
    {
        duplicatedSharedHandle = IntPtr.Zero;
        failureReason = null;
        if (_disposed)
        {
            failureReason = "D3D11 shared texture bridge is disposed.";
            return false;
        }

        if (sourceTexture == IntPtr.Zero)
        {
            failureReason = "D3D11 source texture is missing.";
            return false;
        }

        if (_sharedTexture == IntPtr.Zero || _sharedHandle == IntPtr.Zero)
        {
            failureReason = "D3D11 shared texture bridge is not initialized.";
            return false;
        }

        var mutexAcquired = false;
        try
        {
            // Block the D3D12 consumer from sampling the shared texture while we're mid-copy;
            // AcquireSync throws (caught below) if the consumer doesn't hand it back in time,
            // so a stalled/hung consumer drops this frame instead of corrupting the texture.
            if (_keyedMutex is not null)
            {
                _keyedMutex.AcquireSync(ProducerAcquireKey, KeyedMutexTimeoutMilliseconds);
                mutexAcquired = true;
            }

            _copyResource(_context, _sharedTexture, sourceTexture);
            _flush(_context);
            if (!TryDuplicateHandle(_sharedHandle, out duplicatedSharedHandle))
            {
                failureReason = $"DuplicateHandle failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (duplicatedSharedHandle != IntPtr.Zero)
            {
                CloseHandle(duplicatedSharedHandle);
                duplicatedSharedHandle = IntPtr.Zero;
            }

            failureReason = ex.Message;
            return false;
        }
        finally
        {
            if (mutexAcquired)
            {
                try
                {
                    _keyedMutex!.ReleaseSync(ProducerReleaseKey);
                }
                catch
                {
                    // Best-effort hand-off; if this fails the consumer's next AcquireSync will
                    // time out and skip a frame rather than the process crashing here.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keyedMutex?.Dispose();
        _keyedMutex = null;
        if (_sharedHandle != IntPtr.Zero)
        {
            CloseHandle(_sharedHandle);
            _sharedHandle = IntPtr.Zero;
        }

        if (_sharedTexture != IntPtr.Zero)
        {
            Marshal.Release(_sharedTexture);
            _sharedTexture = IntPtr.Zero;
        }

        Marshal.Release(_context);
        Marshal.Release(_device);
    }

    private void CreateSharedTexture(int width, int height)
    {
        var description = new D3D11Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormatNv12,
            SampleDescription = new DxgiSampleDescription
            {
                Count = 1,
                Quality = 0
            },
            Usage = D3D11UsageDefault,
            BindFlags = D3D11BindShaderResource,
            CPUAccessFlags = 0,
            MiscFlags = D3D11ResourceMiscSharedNtHandle | D3D11ResourceMiscSharedKeyedMutex
        };

        var result = _createTexture2D(_device, ref description, IntPtr.Zero, out _sharedTexture);
        MediaFoundationInterop.ThrowIfFailed(result);
        if (_sharedTexture == IntPtr.Zero)
        {
            throw new InvalidOperationException("D3D11 CreateTexture2D returned no shared bridge texture.");
        }

        var keyedMutexId = typeof(IDXGIKeyedMutex).GUID;
        result = Marshal.QueryInterface(_sharedTexture, in keyedMutexId, out var keyedMutexPointer);
        MediaFoundationInterop.ThrowIfFailed(result);
        if (keyedMutexPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("D3D11 bridge texture did not expose IDXGIKeyedMutex.");
        }

        _keyedMutex = new IDXGIKeyedMutex(keyedMutexPointer);

        var dxgiResourcePointer = IntPtr.Zero;
        try
        {
            var dxgiResourceId = typeof(IDXGIResource1).GUID;
            result = Marshal.QueryInterface(_sharedTexture, in dxgiResourceId, out dxgiResourcePointer);
            MediaFoundationInterop.ThrowIfFailed(result);
            if (dxgiResourcePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("D3D11 bridge texture did not expose IDXGIResource1.");
            }

            using var dxgiResource = new IDXGIResource1(dxgiResourcePointer);
            dxgiResourcePointer = IntPtr.Zero;
            _sharedHandle = dxgiResource.CreateSharedHandle(null, SharedResourceFlags.Read, null);
            if (_sharedHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("D3D11 bridge texture did not create a shared handle.");
            }
        }
        finally
        {
            if (dxgiResourcePointer != IntPtr.Zero)
            {
                Marshal.Release(dxgiResourcePointer);
            }
        }
    }

    private static TDelegate GetComMethod<TDelegate>(IntPtr instance, int slot)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }

    private static bool TryDuplicateHandle(IntPtr sourceHandle, out IntPtr duplicatedHandle)
    {
        var currentProcess = GetCurrentProcess();
        return DuplicateHandle(
            currentProcess,
            sourceHandle,
            currentProcess,
            out duplicatedHandle,
            0,
            false,
            DuplicateSameAccess);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(
        IntPtr device,
        ref D3D11Texture2DDescription description,
        IntPtr initialData,
        out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(IntPtr context, IntPtr destinationResource, IntPtr sourceResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FlushDelegate(IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11Texture2DDescription
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public DxgiSampleDescription SampleDescription;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiSampleDescription
    {
        public uint Count;
        public uint Quality;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
