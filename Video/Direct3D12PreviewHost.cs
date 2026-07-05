using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SharpGen.Runtime;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace PodcastWorkbench.Video;

public sealed class Direct3D12PreviewHost : HwndHost, IDisposable
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;

    private IntPtr _hwnd;
    private IntPtr _nativeD3D12Device;
    private Direct3D12SwapChainRenderer? _renderer;
    private string _previewPathDescription = "DX12 preview path pending";
    private bool _disposed;

    public Direct3D12PreviewHost(IntPtr nativeD3D12Device = default)
    {
        _nativeD3D12Device = nativeD3D12Device;
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsReady => _renderer is not null;

    public string DeviceDescription => _renderer?.DeviceDescription ?? "DX12 preview not initialized";

    public string PreviewPathDescription => _previewPathDescription;

    public void RenderBgraFrame(CameraFrame frame, long frameNumber)
    {
        if (_renderer is null)
        {
            return;
        }

        try
        {
            _renderer.RenderBgraFrame(
                frame.BgraBytes,
                frame.Width,
                frame.Height,
                frame.Stride,
                frameNumber);
            ReportPreviewPath("DX12 BGRA upload preview");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 BGRA preview upload failed: {ex.Message}");
        }
    }

    public void RenderProofFrame(TextureNativeFrameInfo frame)
    {
        if (_renderer is null)
        {
            return;
        }

        try
        {
            _renderer.RenderProofFrame(frame.FrameNumber);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 preview render failed: {ex.Message}");
        }
    }

    public void RenderTextureFrame(TextureNativeFrameLease frame, bool denoiseEnabled, double denoiseStrength)
    {
        if (_renderer is null)
        {
            return;
        }

        try
        {
            string? directTextureFailureReason = null;
            if (frame.IsValid
                && string.Equals(frame.DeviceMode, "D3D12", StringComparison.OrdinalIgnoreCase)
                && _renderer.RenderNativeTextureFrame(frame, denoiseEnabled, denoiseStrength, out directTextureFailureReason))
            {
                ReportPreviewPath("direct DX12 texture");
                return;
            }

            if (frame.Nv12PreviewBytes is not null && frame.Nv12PreviewStride > 0)
            {
                if (_renderer.RenderNv12Frame(
                    frame.Nv12PreviewBytes,
                    frame.Width,
                    frame.Height,
                    frame.Nv12PreviewStride,
                    frame.FrameNumber,
                    denoiseEnabled,
                    denoiseStrength))
                {
                    ReportPreviewPath(FormatUploadFallbackPath("DX12 NV12 upload fallback", directTextureFailureReason));
                    return;
                }
            }

            if (frame.BgraPreviewBytes is not null && frame.BgraPreviewStride > 0)
            {
                _renderer.RenderBgraFrame(
                    frame.BgraPreviewBytes,
                    frame.Width,
                    frame.Height,
                    frame.BgraPreviewStride,
                    frame.FrameNumber);
                ReportPreviewPath(FormatUploadFallbackPath("DX12 BGRA upload fallback", directTextureFailureReason));
                return;
            }

            _renderer.RenderProofFrame(frame.FrameNumber);
            ReportPreviewPath(FormatUploadFallbackPath("DX12 proof-frame fallback", directTextureFailureReason));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 camera frame upload failed: {ex.Message}");
        }
    }

    private void ReportPreviewPath(string description)
    {
        if (string.Equals(_previewPathDescription, description, StringComparison.Ordinal))
        {
            return;
        }

        _previewPathDescription = description;
        StatusChanged?.Invoke(this, $"DX12 preview path: {description}");
    }

    private static string FormatUploadFallbackPath(string path, string? directTextureFailureReason)
    {
        if (string.IsNullOrWhiteSpace(directTextureFailureReason))
        {
            return path;
        }

        var reason = directTextureFailureReason.Trim();
        if (reason.Length > 160)
        {
            reason = reason[..157] + "...";
        }

        return $"{path}; direct unavailable: {reason}";
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(
            0,
            "static",
            string.Empty,
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create DX12 preview child window.");
        }

        try
        {
            var nativeDevice = Interlocked.Exchange(ref _nativeD3D12Device, IntPtr.Zero);
            try
            {
                _renderer = new Direct3D12SwapChainRenderer(
                _hwnd,
                    Math.Max(1, (int)ActualWidth),
                    Math.Max(1, (int)ActualHeight),
                    nativeDevice);
                nativeDevice = IntPtr.Zero;
            }
            finally
            {
                if (nativeDevice != IntPtr.Zero)
                {
                    Marshal.Release(nativeDevice);
                }
            }

            StatusChanged?.Invoke(this, $"{_renderer.DeviceDescription} preview surface ready.");
            _renderer.RenderProofFrame(0);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 preview surface unavailable: {ex.Message}");
        }

        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DisposeRenderer();
        if (hwnd.Handle != IntPtr.Zero)
        {
            DestroyWindow(hwnd.Handle);
        }

        _hwnd = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var width = Math.Max(1, (int)ActualWidth);
        var height = Math.Max(1, (int)ActualHeight);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, width, height, SwpNoZOrder | SwpNoActivate);
        try
        {
            _renderer?.Resize(width, height);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 preview resize failed: {ex.Message}");
        }
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeRenderer();
        var nativeDevice = Interlocked.Exchange(ref _nativeD3D12Device, IntPtr.Zero);
        if (nativeDevice != IntPtr.Zero)
        {
            Marshal.Release(nativeDevice);
        }

        base.Dispose();
    }

    private void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    private sealed class Direct3D12SwapChainRenderer : IDisposable
    {
        private const int FrameCount = 2;
        private const int D3D12DefaultShader4ComponentMappingValue = 5768;

        private readonly ID3D12Device _device;
        private readonly ID3D12CommandQueue _commandQueue;
        private readonly IDXGIFactory4 _factory;
        private readonly ID3D12CommandAllocator _commandAllocator;
        private readonly ID3D12GraphicsCommandList _commandList;
        private readonly ID3D12Fence _fence;
        private readonly AutoResetEvent _fenceEvent = new(false);
        private readonly ID3D12DescriptorHeap _rtvHeap;
        private readonly ID3D12DescriptorHeap _srvHeap;
        private readonly int _rtvDescriptorSize;
        private readonly int _srvDescriptorSize;
        private readonly ID3D12Resource?[] _renderTargets = new ID3D12Resource?[FrameCount];
        private ID3D12RootSignature? _previewRootSignature;
        private ID3D12PipelineState? _previewPipelineState;
        private ID3D12RootSignature? _nv12PreviewRootSignature;
        private ID3D12PipelineState? _nv12PreviewPipelineState;
        private ID3D12Resource? _cameraTexture;
        private ID3D12Resource? _cameraUploadBuffer;
        private PlacedSubresourceFootPrint _cameraTextureFootprint;
        private ResourceStates _cameraTextureState;
        private ID3D12Resource? _nv12YTexture;
        private ID3D12Resource? _nv12UvTexture;
        private ID3D12Resource? _nv12YUploadBuffer;
        private ID3D12Resource? _nv12UvUploadBuffer;
        private PlacedSubresourceFootPrint _nv12YFootprint;
        private PlacedSubresourceFootPrint _nv12UvFootprint;
        private ResourceStates _nv12YTextureState;
        private ResourceStates _nv12UvTextureState;
        private IDXGISwapChain3 _swapChain;
        private ulong _fenceValue;
        private ulong _lastSubmittedFrameFenceValue;
        private int _cameraTextureWidth;
        private int _cameraTextureHeight;
        private int _nv12TextureWidth;
        private int _nv12TextureHeight;
        private int _width;
        private int _height;
        private bool _disposed;
        private bool _shaderPreviewUnavailable;
        private bool _nv12PreviewUnavailable;
        private bool _nativeTexturePreviewUnavailable;
        private string? _nativeTexturePreviewFailureReason;
        private readonly bool _usesSharedCaptureDevice;

        public Direct3D12SwapChainRenderer(IntPtr hwnd, int width, int height, IntPtr nativeD3D12Device = default)
        {
            _width = width;
            _height = height;
            if (nativeD3D12Device != IntPtr.Zero)
            {
                _device = new ID3D12Device(nativeD3D12Device);
                _usesSharedCaptureDevice = true;
            }
            else
            {
                _device = D3D12CreateDevice<ID3D12Device>(null, FeatureLevel.Level_12_0);
            }

            _commandQueue = _device.CreateCommandQueue<ID3D12CommandQueue>(
                new CommandQueueDescription(CommandListType.Direct));
            _factory = CreateDXGIFactory2<IDXGIFactory4>(false);

            var swapChainDescription = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = FrameCount,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = SwapChainFlags.None
            };
            using var swapChain = _factory.CreateSwapChainForHwnd(
                _commandQueue,
                hwnd,
                swapChainDescription,
                null,
                null);
            _swapChain = swapChain.QueryInterface<IDXGISwapChain3>();
            _factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

            _rtvHeap = _device.CreateDescriptorHeap<ID3D12DescriptorHeap>(
                new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
            _srvHeap = _device.CreateDescriptorHeap<ID3D12DescriptorHeap>(
                new DescriptorHeapDescription(
                    DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                    3,
                    DescriptorHeapFlags.ShaderVisible));
            _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            _srvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            CreateRenderTargetViews();
            TryCreatePreviewShaderPipeline();
            TryCreateNv12PreviewShaderPipeline();

            _commandAllocator = _device.CreateCommandAllocator<ID3D12CommandAllocator>(CommandListType.Direct);
            _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
                0,
                CommandListType.Direct,
                _commandAllocator,
                null);
            _commandList.Close();
            _fence = _device.CreateFence<ID3D12Fence>(0);
        }

        public string DeviceDescription => _usesSharedCaptureDevice
            ? "Direct3D 12 / DXGI flip model on shared capture device"
            : "Direct3D 12 / DXGI flip model";

        public unsafe void RenderProofFrame(long frameNumber)
        {
            if (_disposed)
            {
                return;
            }

            WaitForSubmittedFrame();
            var frameIndex = (int)_swapChain.CurrentBackBufferIndex;
            var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 render target is not ready.");
            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator);

            var toRenderTarget = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.Present,
                ResourceStates.RenderTarget);
            _commandList.ResourceBarrier([toRenderTarget]);

            var rtvHandle = GetRtvHandle(frameIndex);
            var pulse = (float)((frameNumber % 120) / 120d);
            _commandList.OMSetRenderTargets(rtvHandle, null);
            _commandList.ClearRenderTargetView(
                rtvHandle,
                new Color4(0.02f + pulse * 0.08f, 0.08f, 0.12f + pulse * 0.18f, 1f),
                0,
                null!);

            var toPresent = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.RenderTarget,
                ResourceStates.Present);
            _commandList.ResourceBarrier([toPresent]);
            _commandList.Close();
            _commandQueue.ExecuteCommandList(_commandList);
            _swapChain.Present(0, PresentFlags.None);
            SignalFrameSubmitted();
        }

        public unsafe void RenderBgraFrame(byte[] bgraBytes, int width, int height, int stride, long frameNumber)
        {
            if (_disposed || bgraBytes.Length < stride * height)
            {
                return;
            }

            if (width != _width || height != _height)
            {
                Resize(width, height);
            }

            if (TryRenderBgraFrameWithShader(bgraBytes, width, height, stride))
            {
                return;
            }

            RenderBgraFrameToBackBuffer(bgraBytes, height, stride);
        }

        public unsafe bool RenderNv12Frame(
            byte[] nv12Bytes,
            int width,
            int height,
            int stride,
            long frameNumber,
            bool denoiseEnabled,
            double denoiseStrength)
        {
            var uvHeight = (height + 1) / 2;
            if (_disposed || stride < width || nv12Bytes.Length < stride * height + stride * uvHeight)
            {
                return false;
            }

            if (width != _width || height != _height)
            {
                Resize(width, height);
            }

            return TryRenderNv12FrameWithShader(nv12Bytes, width, height, stride, denoiseEnabled, denoiseStrength);
        }

        public bool RenderNativeTextureFrame(
            TextureNativeFrameLease frame,
            bool denoiseEnabled,
            double denoiseStrength,
            out string? failureReason)
        {
            failureReason = null;
            if (_disposed)
            {
                failureReason = "renderer disposed";
                return false;
            }

            if (!_usesSharedCaptureDevice)
            {
                failureReason = "presenter is not using the capture D3D12 device";
                return false;
            }

            if (_nativeTexturePreviewUnavailable)
            {
                failureReason = _nativeTexturePreviewFailureReason ?? "direct texture rendering disabled after an earlier failure";
                return false;
            }

            if (_nv12PreviewRootSignature is null || _nv12PreviewPipelineState is null)
            {
                failureReason = "NV12 shader pipeline unavailable";
                return false;
            }

            if (frame.Resource == IntPtr.Zero)
            {
                failureReason = "frame texture resource is missing";
                return false;
            }

            if (!frame.MediaSubtype.Contains("NV12", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"media subtype {frame.MediaSubtype} is not NV12";
                return false;
            }

            if (frame.Width != _width || frame.Height != _height)
            {
                Resize(frame.Width, frame.Height);
            }

            try
            {
                Marshal.AddRef(frame.Resource);
                using var nativeResource = new ID3D12Resource(frame.Resource);
                RenderNativeNv12Resource(nativeResource, frame.Width, frame.Height, denoiseEnabled, denoiseStrength);
                _nativeTexturePreviewFailureReason = null;
                return true;
            }
            catch (Exception ex)
            {
                _nativeTexturePreviewUnavailable = true;
                _nativeTexturePreviewFailureReason = ex.Message;
                failureReason = ex.Message;
                return false;
            }
        }

        private void RenderNativeNv12Resource(
            ID3D12Resource cameraResource,
            int width,
            int height,
            bool denoiseEnabled,
            double denoiseStrength)
        {
            var ySrvDescription = new ShaderResourceViewDescription
            {
                Format = Format.R8_UNorm,
                ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = D3D12DefaultShader4ComponentMappingValue,
                Texture2D = new Texture2DShaderResourceView
                {
                    MipLevels = 1,
                    PlaneSlice = 0
                }
            };
            var uvSrvDescription = new ShaderResourceViewDescription
            {
                Format = Format.R8G8_UNorm,
                ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = D3D12DefaultShader4ComponentMappingValue,
                Texture2D = new Texture2DShaderResourceView
                {
                    MipLevels = 1,
                    PlaneSlice = 1
                }
            };
            _device.CreateShaderResourceView(cameraResource, ySrvDescription, GetSrvCpuHandle(1));
            _device.CreateShaderResourceView(cameraResource, uvSrvDescription, GetSrvCpuHandle(2));

            WaitForSubmittedFrame();
            var frameIndex = (int)_swapChain.CurrentBackBufferIndex;
            var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 render target is not ready.");
            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator);

            var cameraToPixelShaderResource = ResourceBarrier.BarrierTransition(
                cameraResource,
                ResourceStates.Common,
                ResourceStates.PixelShaderResource);
            var toRenderTarget = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.Present,
                ResourceStates.RenderTarget);
            _commandList.ResourceBarrier([cameraToPixelShaderResource, toRenderTarget]);

            var rtvHandle = GetRtvHandle(frameIndex);
            _commandList.SetGraphicsRootSignature(_nv12PreviewRootSignature);
            _commandList.SetPipelineState(_nv12PreviewPipelineState);
            _commandList.SetDescriptorHeaps([_srvHeap]);
            _commandList.SetGraphicsRootDescriptorTable(0, GetSrvGpuHandle(1));
            SetNv12DenoiseConstants(width, height, denoiseEnabled, denoiseStrength);
            var viewport = new Viewport(0, 0, _width, _height);
            var scissor = new RawRect(0, 0, _width, _height);
            _commandList.RSSetViewports(viewport);
            _commandList.RSSetScissorRects(scissor);
            _commandList.OMSetRenderTargets(rtvHandle, null);
            _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _commandList.DrawInstanced(3, 1, 0, 0);

            var toPresent = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.RenderTarget,
                ResourceStates.Present);
            var cameraToCommon = ResourceBarrier.BarrierTransition(
                cameraResource,
                ResourceStates.PixelShaderResource,
                ResourceStates.Common);
            _commandList.ResourceBarrier([toPresent, cameraToCommon]);
            _commandList.Close();
            _commandQueue.ExecuteCommandList(_commandList);
            _swapChain.Present(0, PresentFlags.None);
            SignalFrameSubmitted();
        }

        private unsafe bool TryRenderNv12FrameWithShader(
            byte[] nv12Bytes,
            int width,
            int height,
            int stride,
            bool denoiseEnabled,
            double denoiseStrength)
        {
            if (_nv12PreviewUnavailable || _nv12PreviewRootSignature is null || _nv12PreviewPipelineState is null)
            {
                return false;
            }

            try
            {
                EnsureNv12Textures(width, height);
                var yTexture = _nv12YTexture;
                var uvTexture = _nv12UvTexture;
                var yUploadBuffer = _nv12YUploadBuffer;
                var uvUploadBuffer = _nv12UvUploadBuffer;
                if (yTexture is null || uvTexture is null || yUploadBuffer is null || uvUploadBuffer is null)
                {
                    return false;
                }

                WaitForSubmittedFrame();
                CopyNv12FrameToUploadBuffers(yUploadBuffer, uvUploadBuffer, nv12Bytes, width, height, stride);

                var frameIndex = (int)_swapChain.CurrentBackBufferIndex;
                var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 render target is not ready.");
                _commandAllocator.Reset();
                _commandList.Reset(_commandAllocator);

                if (_nv12YTextureState != ResourceStates.CopyDest)
                {
                    var toCopyDestination = ResourceBarrier.BarrierTransition(
                        yTexture,
                        _nv12YTextureState,
                        ResourceStates.CopyDest);
                    _commandList.ResourceBarrier([toCopyDestination]);
                    _nv12YTextureState = ResourceStates.CopyDest;
                }

                if (_nv12UvTextureState != ResourceStates.CopyDest)
                {
                    var toCopyDestination = ResourceBarrier.BarrierTransition(
                        uvTexture,
                        _nv12UvTextureState,
                        ResourceStates.CopyDest);
                    _commandList.ResourceBarrier([toCopyDestination]);
                    _nv12UvTextureState = ResourceStates.CopyDest;
                }

                _commandList.CopyTextureRegion(
                    new TextureCopyLocation(yTexture, 0),
                    0,
                    0,
                    0,
                    new TextureCopyLocation(yUploadBuffer, _nv12YFootprint),
                    null);
                _commandList.CopyTextureRegion(
                    new TextureCopyLocation(uvTexture, 0),
                    0,
                    0,
                    0,
                    new TextureCopyLocation(uvUploadBuffer, _nv12UvFootprint),
                    null);

                var yToPixelShaderResource = ResourceBarrier.BarrierTransition(
                    yTexture,
                    ResourceStates.CopyDest,
                    ResourceStates.PixelShaderResource);
                var uvToPixelShaderResource = ResourceBarrier.BarrierTransition(
                    uvTexture,
                    ResourceStates.CopyDest,
                    ResourceStates.PixelShaderResource);
                _commandList.ResourceBarrier([yToPixelShaderResource, uvToPixelShaderResource]);
                _nv12YTextureState = ResourceStates.PixelShaderResource;
                _nv12UvTextureState = ResourceStates.PixelShaderResource;

                var toRenderTarget = ResourceBarrier.BarrierTransition(
                    renderTarget,
                    ResourceStates.Present,
                    ResourceStates.RenderTarget);
                _commandList.ResourceBarrier([toRenderTarget]);

                var rtvHandle = GetRtvHandle(frameIndex);
                _commandList.SetGraphicsRootSignature(_nv12PreviewRootSignature);
                _commandList.SetPipelineState(_nv12PreviewPipelineState);
                _commandList.SetDescriptorHeaps([_srvHeap]);
                _commandList.SetGraphicsRootDescriptorTable(0, GetSrvGpuHandle(1));
                SetNv12DenoiseConstants(width, height, denoiseEnabled, denoiseStrength);
                var viewport = new Viewport(0, 0, _width, _height);
                var scissor = new RawRect(0, 0, _width, _height);
                _commandList.RSSetViewports(viewport);
                _commandList.RSSetScissorRects(scissor);
                _commandList.OMSetRenderTargets(rtvHandle, null);
                _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _commandList.DrawInstanced(3, 1, 0, 0);

                var toPresent = ResourceBarrier.BarrierTransition(
                    renderTarget,
                    ResourceStates.RenderTarget,
                    ResourceStates.Present);
                _commandList.ResourceBarrier([toPresent]);
                _commandList.Close();
                _commandQueue.ExecuteCommandList(_commandList);
                _swapChain.Present(0, PresentFlags.None);
                SignalFrameSubmitted();
                return true;
            }
            catch
            {
                _nv12PreviewUnavailable = true;
                return false;
            }
        }

        private unsafe bool TryRenderBgraFrameWithShader(byte[] bgraBytes, int width, int height, int stride)
        {
            if (_shaderPreviewUnavailable || _previewRootSignature is null || _previewPipelineState is null)
            {
                return false;
            }

            try
            {
                EnsureCameraTexture(width, height);
                var cameraTexture = _cameraTexture;
                var uploadBuffer = _cameraUploadBuffer;
                if (cameraTexture is null || uploadBuffer is null)
                {
                    return false;
                }

                WaitForSubmittedFrame();
                CopyBgraFrameToUploadBuffer(uploadBuffer, bgraBytes, width, height, stride);

                var frameIndex = (int)_swapChain.CurrentBackBufferIndex;
                var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 render target is not ready.");
                _commandAllocator.Reset();
                _commandList.Reset(_commandAllocator);

                if (_cameraTextureState != ResourceStates.CopyDest)
                {
                    var toCopyDestination = ResourceBarrier.BarrierTransition(
                        cameraTexture,
                        _cameraTextureState,
                        ResourceStates.CopyDest);
                    _commandList.ResourceBarrier([toCopyDestination]);
                    _cameraTextureState = ResourceStates.CopyDest;
                }

                _commandList.CopyTextureRegion(
                    new TextureCopyLocation(cameraTexture, 0),
                    0,
                    0,
                    0,
                    new TextureCopyLocation(uploadBuffer, _cameraTextureFootprint),
                    null);

                var toPixelShaderResource = ResourceBarrier.BarrierTransition(
                    cameraTexture,
                    ResourceStates.CopyDest,
                    ResourceStates.PixelShaderResource);
                _commandList.ResourceBarrier([toPixelShaderResource]);
                _cameraTextureState = ResourceStates.PixelShaderResource;

                var toRenderTarget = ResourceBarrier.BarrierTransition(
                    renderTarget,
                    ResourceStates.Present,
                    ResourceStates.RenderTarget);
                _commandList.ResourceBarrier([toRenderTarget]);

                var rtvHandle = GetRtvHandle(frameIndex);
                _commandList.SetGraphicsRootSignature(_previewRootSignature);
                _commandList.SetPipelineState(_previewPipelineState);
                _commandList.SetDescriptorHeaps([_srvHeap]);
                _commandList.SetGraphicsRootDescriptorTable(0, _srvHeap.GetGPUDescriptorHandleForHeapStart());
                var viewport = new Viewport(0, 0, _width, _height);
                var scissor = new RawRect(0, 0, _width, _height);
                _commandList.RSSetViewports(viewport);
                _commandList.RSSetScissorRects(scissor);
                _commandList.OMSetRenderTargets(rtvHandle, null);
                _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _commandList.DrawInstanced(3, 1, 0, 0);

                var toPresent = ResourceBarrier.BarrierTransition(
                    renderTarget,
                    ResourceStates.RenderTarget,
                    ResourceStates.Present);
                _commandList.ResourceBarrier([toPresent]);
                _commandList.Close();
                _commandQueue.ExecuteCommandList(_commandList);
                _swapChain.Present(0, PresentFlags.None);
                SignalFrameSubmitted();
                return true;
            }
            catch
            {
                _shaderPreviewUnavailable = true;
                return false;
            }
        }

        private unsafe void RenderBgraFrameToBackBuffer(byte[] bgraBytes, int height, int stride)
        {
            WaitForSubmittedFrame();
            var frameIndex = (int)_swapChain.CurrentBackBufferIndex;
            var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 render target is not ready.");
            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator);

            var toCommon = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.Present,
                ResourceStates.Common);
            _commandList.ResourceBarrier([toCommon]);
            _commandList.Close();
            _commandQueue.ExecuteCommandList(_commandList);
            WaitForGpu();

            fixed (byte* source = bgraBytes)
            {
                renderTarget.WriteToSubresource(
                    0,
                    null,
                    (IntPtr)source,
                    (uint)stride,
                    (uint)(stride * height));
            }

            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator);
            var toPresent = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.Common,
                ResourceStates.Present);
            _commandList.ResourceBarrier([toPresent]);
            _commandList.Close();
            _commandQueue.ExecuteCommandList(_commandList);
            _swapChain.Present(0, PresentFlags.None);
            SignalFrameSubmitted();
        }

        private void EnsureCameraTexture(int width, int height)
        {
            if (_cameraTexture is not null
                && _cameraUploadBuffer is not null
                && _cameraTextureWidth == width
                && _cameraTextureHeight == height)
            {
                return;
            }

            WaitForGpu();
            _cameraTexture?.Dispose();
            _cameraUploadBuffer?.Dispose();
            _cameraTexture = null;
            _cameraUploadBuffer = null;
            _cameraTextureWidth = width;
            _cameraTextureHeight = height;
            var description = new ResourceDescription(
                ResourceDimension.Texture2D,
                0,
                (ulong)width,
                (uint)height,
                1,
                1,
                Format.B8G8R8A8_UNorm,
                1,
                0,
                TextureLayout.Unknown,
                ResourceFlags.None);
            _cameraTexture = _device.CreateCommittedResource<ID3D12Resource>(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                description,
                ResourceStates.CopyDest);
            _cameraTextureState = ResourceStates.CopyDest;
            var layouts = new PlacedSubresourceFootPrint[1];
            var numRows = new uint[1];
            var rowSizesInBytes = new ulong[1];
            _device.GetCopyableFootprints(
                description,
                0,
                1,
                0,
                layouts,
                numRows,
                rowSizesInBytes,
                out var uploadBytes);
            _cameraTextureFootprint = layouts[0];
            _cameraUploadBuffer = _device.CreateCommittedResource<ID3D12Resource>(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(uploadBytes, ResourceFlags.None, 0),
                ResourceStates.GenericRead);
            var srvDescription = new ShaderResourceViewDescription
            {
                Format = Format.B8G8R8A8_UNorm,
                ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = D3D12DefaultShader4ComponentMappingValue,
                Texture2D = new Texture2DShaderResourceView
                {
                    MipLevels = 1
                }
            };
            _device.CreateShaderResourceView(
                _cameraTexture,
                srvDescription,
                _srvHeap.GetCPUDescriptorHandleForHeapStart());
        }

        private void EnsureNv12Textures(int width, int height)
        {
            if (_nv12YTexture is not null
                && _nv12UvTexture is not null
                && _nv12YUploadBuffer is not null
                && _nv12UvUploadBuffer is not null
                && _nv12TextureWidth == width
                && _nv12TextureHeight == height)
            {
                return;
            }

            WaitForGpu();
            _nv12YTexture?.Dispose();
            _nv12UvTexture?.Dispose();
            _nv12YUploadBuffer?.Dispose();
            _nv12UvUploadBuffer?.Dispose();
            _nv12YTexture = null;
            _nv12UvTexture = null;
            _nv12YUploadBuffer = null;
            _nv12UvUploadBuffer = null;
            _nv12TextureWidth = width;
            _nv12TextureHeight = height;

            var yDescription = new ResourceDescription(
                ResourceDimension.Texture2D,
                0,
                (ulong)width,
                (uint)height,
                1,
                1,
                Format.R8_UNorm,
                1,
                0,
                TextureLayout.Unknown,
                ResourceFlags.None);
            var uvDescription = new ResourceDescription(
                ResourceDimension.Texture2D,
                0,
                (ulong)Math.Max(1, width / 2),
                (uint)Math.Max(1, height / 2),
                1,
                1,
                Format.R8G8_UNorm,
                1,
                0,
                TextureLayout.Unknown,
                ResourceFlags.None);

            _nv12YTexture = _device.CreateCommittedResource<ID3D12Resource>(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                yDescription,
                ResourceStates.CopyDest);
            _nv12UvTexture = _device.CreateCommittedResource<ID3D12Resource>(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                uvDescription,
                ResourceStates.CopyDest);
            _nv12YTextureState = ResourceStates.CopyDest;
            _nv12UvTextureState = ResourceStates.CopyDest;

            _nv12YFootprint = CreateUploadBufferForTexture(yDescription, out _nv12YUploadBuffer);
            _nv12UvFootprint = CreateUploadBufferForTexture(uvDescription, out _nv12UvUploadBuffer);

            var ySrvDescription = new ShaderResourceViewDescription
            {
                Format = Format.R8_UNorm,
                ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = D3D12DefaultShader4ComponentMappingValue,
                Texture2D = new Texture2DShaderResourceView
                {
                    MipLevels = 1
                }
            };
            var uvSrvDescription = new ShaderResourceViewDescription
            {
                Format = Format.R8G8_UNorm,
                ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = D3D12DefaultShader4ComponentMappingValue,
                Texture2D = new Texture2DShaderResourceView
                {
                    MipLevels = 1
                }
            };
            _device.CreateShaderResourceView(_nv12YTexture, ySrvDescription, GetSrvCpuHandle(1));
            _device.CreateShaderResourceView(_nv12UvTexture, uvSrvDescription, GetSrvCpuHandle(2));
        }

        private PlacedSubresourceFootPrint CreateUploadBufferForTexture(
            ResourceDescription description,
            out ID3D12Resource uploadBuffer)
        {
            var layouts = new PlacedSubresourceFootPrint[1];
            var numRows = new uint[1];
            var rowSizesInBytes = new ulong[1];
            _device.GetCopyableFootprints(
                description,
                0,
                1,
                0,
                layouts,
                numRows,
                rowSizesInBytes,
                out var uploadBytes);
            uploadBuffer = _device.CreateCommittedResource<ID3D12Resource>(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(uploadBytes, ResourceFlags.None, 0),
                ResourceStates.GenericRead);
            return layouts[0];
        }

        private unsafe void CopyBgraFrameToUploadBuffer(ID3D12Resource uploadBuffer, byte[] bgraBytes, int width, int height, int stride)
        {
            void* mappedPointer = null;
            uploadBuffer.Map(0, null, &mappedPointer).CheckError();
            var mappedData = (byte*)mappedPointer;
            try
            {
                fixed (byte* sourceStart = bgraBytes)
                {
                    var sourceRowBytes = width * 4;
                    var destinationStart = mappedData + (nint)_cameraTextureFootprint.Offset;
                    var destinationStride = (nint)_cameraTextureFootprint.Footprint.RowPitch;
                    for (var row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(
                            sourceStart + row * stride,
                            destinationStart + row * destinationStride,
                            destinationStride,
                            sourceRowBytes);
                    }
                }
            }
            finally
            {
                uploadBuffer.Unmap(0, null);
            }
        }

        private unsafe void CopyNv12FrameToUploadBuffers(
            ID3D12Resource yUploadBuffer,
            ID3D12Resource uvUploadBuffer,
            byte[] nv12Bytes,
            int width,
            int height,
            int stride)
        {
            var uvHeight = Math.Max(1, height / 2);
            var uvOffset = stride * height;
            void* mappedYPointer = null;
            void* mappedUvPointer = null;
            yUploadBuffer.Map(0, null, &mappedYPointer).CheckError();
            uvUploadBuffer.Map(0, null, &mappedUvPointer).CheckError();
            try
            {
                fixed (byte* sourceStart = nv12Bytes)
                {
                    var yDestinationStart = (byte*)mappedYPointer + (nint)_nv12YFootprint.Offset;
                    var yDestinationStride = (nint)_nv12YFootprint.Footprint.RowPitch;
                    for (var row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(
                            sourceStart + row * stride,
                            yDestinationStart + row * yDestinationStride,
                            yDestinationStride,
                            width);
                    }

                    var uvDestinationStart = (byte*)mappedUvPointer + (nint)_nv12UvFootprint.Offset;
                    var uvDestinationStride = (nint)_nv12UvFootprint.Footprint.RowPitch;
                    for (var row = 0; row < uvHeight; row++)
                    {
                        Buffer.MemoryCopy(
                            sourceStart + uvOffset + row * stride,
                            uvDestinationStart + row * uvDestinationStride,
                            uvDestinationStride,
                            width);
                    }
                }
            }
            finally
            {
                uvUploadBuffer.Unmap(0, null);
                yUploadBuffer.Unmap(0, null);
            }
        }

        private void SetNv12DenoiseConstants(int width, int height, bool denoiseEnabled, double denoiseStrength)
        {
            var strength = (float)Math.Clamp(denoiseStrength, 0.5d, 8d);
            var amount = denoiseEnabled ? Math.Clamp(strength / 8f, 0.06f, 1f) : 0f;
            var edgeThreshold = denoiseEnabled ? Math.Clamp(0.018f + strength * 0.0045f, 0.02f, 0.07f) : 0f;
            _commandList.SetGraphicsRoot32BitConstant(1, BitConverter.SingleToUInt32Bits(amount), 0);
            _commandList.SetGraphicsRoot32BitConstant(1, BitConverter.SingleToUInt32Bits(edgeThreshold), 1);
            _commandList.SetGraphicsRoot32BitConstant(1, BitConverter.SingleToUInt32Bits(1f / Math.Max(1, width)), 2);
            _commandList.SetGraphicsRoot32BitConstant(1, BitConverter.SingleToUInt32Bits(1f / Math.Max(1, height)), 3);
        }

        private void TryCreatePreviewShaderPipeline()
        {
            try
            {
                var vertexShader = CompileShader("VSMain", "vs_5_0");
                var pixelShader = CompileShader("PSMain", "ps_5_0");
                var ranges = new[]
                {
                    new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)
                };
                var parameters = new[]
                {
                    new RootParameter(new RootDescriptorTable(ranges), ShaderVisibility.Pixel)
                };
                var samplers = new[]
                {
                    new StaticSamplerDescription(
                        0,
                        Filter.MinMagMipLinear,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        0,
                        0,
                        ComparisonFunction.Never,
                        StaticBorderColor.TransparentBlack,
                        0,
                        float.MaxValue,
                        ShaderVisibility.Pixel,
                        0)
                };
                var rootDescription = new RootSignatureDescription(
                    RootSignatureFlags.AllowInputAssemblerInputLayout,
                    parameters,
                    samplers);
                _previewRootSignature = _device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);

                var pipelineDescription = new GraphicsPipelineStateDescription
                {
                    RootSignature = _previewRootSignature,
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                    BlendState = BlendDescription.Opaque,
                    RasterizerState = RasterizerDescription.CullNone,
                    DepthStencilState = DepthStencilDescription.None,
                    SampleMask = uint.MaxValue,
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                    RenderTargetFormats = [Format.B8G8R8A8_UNorm],
                    SampleDescription = new SampleDescription(1, 0)
                };
                _previewPipelineState = _device.CreateGraphicsPipelineState<ID3D12PipelineState>(pipelineDescription);
            }
            catch
            {
                _shaderPreviewUnavailable = true;
            }
        }

        private void TryCreateNv12PreviewShaderPipeline()
        {
            try
            {
                var vertexShader = CompileShader(Nv12PreviewShaderSource, "VSMain", "vs_5_0");
                var pixelShader = CompileShader(Nv12PreviewShaderSource, "PSMain", "ps_5_0");
                var ranges = new[]
                {
                    new DescriptorRange(DescriptorRangeType.ShaderResourceView, 2, 0)
                };
                var parameters = new[]
                {
                    new RootParameter(new RootDescriptorTable(ranges), ShaderVisibility.Pixel),
                    new RootParameter(new RootConstants(4, 0, 0), ShaderVisibility.Pixel)
                };
                var samplers = new[]
                {
                    new StaticSamplerDescription(
                        0,
                        Filter.MinMagMipLinear,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        0,
                        0,
                        ComparisonFunction.Never,
                        StaticBorderColor.TransparentBlack,
                        0,
                        float.MaxValue,
                        ShaderVisibility.Pixel,
                        0)
                };
                var rootDescription = new RootSignatureDescription(
                    RootSignatureFlags.AllowInputAssemblerInputLayout,
                    parameters,
                    samplers);
                _nv12PreviewRootSignature = _device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);

                var pipelineDescription = new GraphicsPipelineStateDescription
                {
                    RootSignature = _nv12PreviewRootSignature,
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                    BlendState = BlendDescription.Opaque,
                    RasterizerState = RasterizerDescription.CullNone,
                    DepthStencilState = DepthStencilDescription.None,
                    SampleMask = uint.MaxValue,
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                    RenderTargetFormats = [Format.B8G8R8A8_UNorm],
                    SampleDescription = new SampleDescription(1, 0)
                };
                _nv12PreviewPipelineState = _device.CreateGraphicsPipelineState<ID3D12PipelineState>(pipelineDescription);
            }
            catch
            {
                _nv12PreviewUnavailable = true;
            }
        }

        private static byte[] CompileShader(string entryPoint, string profile)
        {
            return CompileShader(PreviewShaderSource, entryPoint, profile);
        }

        private static byte[] CompileShader(string shaderSource, string entryPoint, string profile)
        {
            return Compiler.Compile(
                    shaderSource,
                    entryPoint,
                    "PodcastWorkbenchPreview.hlsl",
                    profile,
                    ShaderFlags.OptimizationLevel3,
                    EffectFlags.None)
                .ToArray();
        }

        private const string PreviewShaderSource = """
            Texture2D<float4> CameraFrame : register(t0);
            SamplerState CameraSampler : register(s0);

            struct VertexOutput
            {
                float4 Position : SV_POSITION;
                float2 TexCoord : TEXCOORD0;
            };

            VertexOutput VSMain(uint vertexId : SV_VertexID)
            {
                float2 positions[3] =
                {
                    float2(-1.0, -1.0),
                    float2(-1.0, 3.0),
                    float2(3.0, -1.0)
                };
                float2 texCoords[3] =
                {
                    float2(0.0, 1.0),
                    float2(0.0, -1.0),
                    float2(2.0, 1.0)
                };

                VertexOutput output;
                output.Position = float4(positions[vertexId], 0.0, 1.0);
                output.TexCoord = texCoords[vertexId];
                return output;
            }

            float4 PSMain(VertexOutput input) : SV_TARGET
            {
                float4 color = CameraFrame.Sample(CameraSampler, input.TexCoord);
                return float4(saturate(color.rgb), 1.0);
            }
            """;

        private const string Nv12PreviewShaderSource = """
            Texture2D<float> CameraLuma : register(t0);
            Texture2D<float2> CameraChroma : register(t1);
            SamplerState CameraSampler : register(s0);

            cbuffer PreviewSettings : register(b0)
            {
                float DenoiseAmount;
                float DenoiseEdgeThreshold;
                float2 TexelSize;
            };

            struct VertexOutput
            {
                float4 Position : SV_POSITION;
                float2 TexCoord : TEXCOORD0;
            };

            VertexOutput VSMain(uint vertexId : SV_VertexID)
            {
                float2 positions[3] =
                {
                    float2(-1.0, -1.0),
                    float2(-1.0, 3.0),
                    float2(3.0, -1.0)
                };
                float2 texCoords[3] =
                {
                    float2(0.0, 1.0),
                    float2(0.0, -1.0),
                    float2(2.0, 1.0)
                };

                VertexOutput output;
                output.Position = float4(positions[vertexId], 0.0, 1.0);
                output.TexCoord = texCoords[vertexId];
                return output;
            }

            float NormalizeLuma(float rawY)
            {
                return saturate((rawY - (16.0 / 255.0)) * (255.0 / 219.0));
            }

            float EdgeAwareWeight(float centerY, float sampleY)
            {
                float distance = abs(sampleY - centerY);
                return saturate(1.0 - distance / max(DenoiseEdgeThreshold, 0.0001));
            }

            float DenoiseLuma(float2 texCoord, float centerY)
            {
                if (DenoiseAmount <= 0.001)
                {
                    return centerY;
                }

                float2 offsets[4] =
                {
                    float2(TexelSize.x, 0.0),
                    float2(-TexelSize.x, 0.0),
                    float2(0.0, TexelSize.y),
                    float2(0.0, -TexelSize.y)
                };

                float weightedSum = centerY;
                float weightSum = 1.0;
                for (int i = 0; i < 4; i++)
                {
                    float sampleY = NormalizeLuma(CameraLuma.Sample(CameraSampler, texCoord + offsets[i]));
                    float weight = EdgeAwareWeight(centerY, sampleY);
                    weightedSum += sampleY * weight;
                    weightSum += weight;
                }

                float smoothed = weightedSum / weightSum;
                return lerp(centerY, smoothed, saturate(DenoiseAmount * 0.72));
            }

            float2 DenoiseChroma(float2 texCoord, float2 centerUv)
            {
                if (DenoiseAmount <= 0.001)
                {
                    return centerUv;
                }

                float2 chromaTexel = TexelSize * 2.0;
                float2 smoothed =
                    centerUv +
                    CameraChroma.Sample(CameraSampler, texCoord + float2(chromaTexel.x, 0.0)) +
                    CameraChroma.Sample(CameraSampler, texCoord + float2(-chromaTexel.x, 0.0)) +
                    CameraChroma.Sample(CameraSampler, texCoord + float2(0.0, chromaTexel.y)) +
                    CameraChroma.Sample(CameraSampler, texCoord + float2(0.0, -chromaTexel.y));
                smoothed *= 0.2;
                return lerp(centerUv, smoothed, saturate(DenoiseAmount * 0.35));
            }

            float4 PSMain(VertexOutput input) : SV_TARGET
            {
                float centerY = NormalizeLuma(CameraLuma.Sample(CameraSampler, input.TexCoord));
                float y = DenoiseLuma(input.TexCoord, centerY);
                float2 uv = DenoiseChroma(input.TexCoord, CameraChroma.Sample(CameraSampler, input.TexCoord)) - float2(0.5, 0.5);
                float3 rgb = float3(
                    y + 1.5748 * uv.y,
                    y - 0.1873 * uv.x - 0.4681 * uv.y,
                    y + 1.8556 * uv.x);
                return float4(saturate(rgb), 1.0);
            }
            """;

        public void Resize(int width, int height)
        {
            if (_disposed || width == _width && height == _height)
            {
                return;
            }

            WaitForGpu();
            ReleaseRenderTargets();
            _swapChain.ResizeBuffers(FrameCount, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            _width = width;
            _height = height;
            CreateRenderTargetViews();
            RenderProofFrame(0);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            WaitForGpu();
            _disposed = true;
            ReleaseRenderTargets();
            _swapChain.Dispose();
            _rtvHeap.Dispose();
            _srvHeap.Dispose();
            _cameraTexture?.Dispose();
            _cameraUploadBuffer?.Dispose();
            _nv12YTexture?.Dispose();
            _nv12UvTexture?.Dispose();
            _nv12YUploadBuffer?.Dispose();
            _nv12UvUploadBuffer?.Dispose();
            _previewPipelineState?.Dispose();
            _previewRootSignature?.Dispose();
            _nv12PreviewPipelineState?.Dispose();
            _nv12PreviewRootSignature?.Dispose();
            _fence.Dispose();
            _fenceEvent.Dispose();
            _commandList.Dispose();
            _commandAllocator.Dispose();
            _factory.Dispose();
            _commandQueue.Dispose();
            _device.Dispose();
        }

        private void CreateRenderTargetViews()
        {
            var handle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
            for (var i = 0; i < FrameCount; i++)
            {
                _renderTargets[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
                _device.CreateRenderTargetView(_renderTargets[i], null, handle);
                handle += _rtvDescriptorSize;
            }
        }

        private CpuDescriptorHandle GetRtvHandle(int frameIndex)
        {
            return _rtvHeap.GetCPUDescriptorHandleForHeapStart() + frameIndex * _rtvDescriptorSize;
        }

        private CpuDescriptorHandle GetSrvCpuHandle(int descriptorIndex)
        {
            return _srvHeap.GetCPUDescriptorHandleForHeapStart() + descriptorIndex * _srvDescriptorSize;
        }

        private GpuDescriptorHandle GetSrvGpuHandle(int descriptorIndex)
        {
            return _srvHeap.GetGPUDescriptorHandleForHeapStart() + descriptorIndex * _srvDescriptorSize;
        }

        private void ReleaseRenderTargets()
        {
            for (var i = 0; i < _renderTargets.Length; i++)
            {
                _renderTargets[i]?.Dispose();
                _renderTargets[i] = null;
            }
        }

        private void WaitForGpu()
        {
            if (_disposed)
            {
                return;
            }

            _fenceValue++;
            _commandQueue.Signal(_fence, _fenceValue);
            if (_fence.CompletedValue >= _fenceValue)
            {
                return;
            }

            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            _fenceEvent.WaitOne();
            _lastSubmittedFrameFenceValue = 0;
        }

        private void SignalFrameSubmitted()
        {
            if (_disposed)
            {
                return;
            }

            _fenceValue++;
            _commandQueue.Signal(_fence, _fenceValue);
            _lastSubmittedFrameFenceValue = _fenceValue;
        }

        private void WaitForSubmittedFrame()
        {
            if (_disposed || _lastSubmittedFrameFenceValue == 0)
            {
                return;
            }

            if (_fence.CompletedValue < _lastSubmittedFrameFenceValue)
            {
                _fence.SetEventOnCompletion(_lastSubmittedFrameFenceValue, _fenceEvent);
                _fenceEvent.WaitOne();
            }

            _lastSubmittedFrameFenceValue = 0;
        }
    }
}
