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
    private Direct3D12SwapChainRenderer? _renderer;
    private bool _disposed;

    public event EventHandler<string>? StatusChanged;

    public bool IsReady => _renderer is not null;

    public string DeviceDescription => _renderer?.DeviceDescription ?? "DX12 preview not initialized";

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

    public void RenderBgraFrame(TextureNativeFrameLease frame)
    {
        if (_renderer is null || frame.BgraPreviewBytes is null || frame.BgraPreviewStride <= 0)
        {
            return;
        }

        try
        {
            _renderer.RenderBgraFrame(
                frame.BgraPreviewBytes,
                frame.Width,
                frame.Height,
                frame.BgraPreviewStride,
                frame.FrameNumber);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 camera frame upload failed: {ex.Message}");
        }
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
            _renderer = new Direct3D12SwapChainRenderer(
                _hwnd,
                Math.Max(1, (int)ActualWidth),
                Math.Max(1, (int)ActualHeight));
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
        private readonly ID3D12Resource?[] _renderTargets = new ID3D12Resource?[FrameCount];
        private ID3D12RootSignature? _previewRootSignature;
        private ID3D12PipelineState? _previewPipelineState;
        private ID3D12Resource? _cameraTexture;
        private ID3D12Resource? _cameraUploadBuffer;
        private PlacedSubresourceFootPrint _cameraTextureFootprint;
        private ResourceStates _cameraTextureState;
        private IDXGISwapChain3 _swapChain;
        private ulong _fenceValue;
        private int _cameraTextureWidth;
        private int _cameraTextureHeight;
        private int _width;
        private int _height;
        private bool _disposed;
        private bool _shaderPreviewUnavailable;

        public Direct3D12SwapChainRenderer(IntPtr hwnd, int width, int height)
        {
            _width = width;
            _height = height;
            _device = D3D12CreateDevice<ID3D12Device>(null, FeatureLevel.Level_12_0);
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
                    1,
                    DescriptorHeapFlags.ShaderVisible));
            _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            CreateRenderTargetViews();
            TryCreatePreviewShaderPipeline();

            _commandAllocator = _device.CreateCommandAllocator<ID3D12CommandAllocator>(CommandListType.Direct);
            _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
                0,
                CommandListType.Direct,
                _commandAllocator,
                null);
            _commandList.Close();
            _fence = _device.CreateFence<ID3D12Fence>(0);
        }

        public string DeviceDescription => "Direct3D 12 / DXGI flip model";

        public unsafe void RenderProofFrame(long frameNumber)
        {
            if (_disposed)
            {
                return;
            }

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
            WaitForGpu();
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
                WaitForGpu();
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
            WaitForGpu();
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

        private static byte[] CompileShader(string entryPoint, string profile)
        {
            return Compiler.Compile(
                    PreviewShaderSource,
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
            _previewPipelineState?.Dispose();
            _previewRootSignature?.Dispose();
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
        }
    }
}
