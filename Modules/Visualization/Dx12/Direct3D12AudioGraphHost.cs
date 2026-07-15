using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using JerichoDown.Audio;
using JerichoDown.Modules.Visualization;
using SharpGen.Runtime;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace JerichoDown.Modules.Visualization.Dx12;

public enum Direct3D12AudioGraphMode
{
    Waterfall,
    SelectedMicSpectrum,
    ProgramOutputSpectrum,
    MicrophoneSpectrumLines
}

public sealed class Direct3D12AudioGraphHost : HwndHost, IDisposable
{
    public const int DefaultWaterfallLinesPerGap = 8;
    public const int MinimumWaterfallLinesPerGap = 0;
    public const int MaximumWaterfallLinesPerGap = 16;

    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;
    private const int SsBlackRect = 0x00000004;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;

    private readonly object _rendererLock = new();
    private readonly object _renderWorkerLock = new();
    private readonly object _spectrumHoverLock = new();
    private readonly AutoResetEvent _renderFrameReady = new(false);
    private Thread? _renderThread;
    private SpectrumFrame? _pendingFrame;
    private Direct3D12AudioGraphRenderer? _renderer;
    private IntPtr _hwnd;
    private int _graphMode;
    private int _waterfallLinesPerGap = DefaultWaterfallLinesPerGap;
    private float _spectrumHoverStart = float.NaN;
    private float _spectrumHoverEnd = float.NaN;
    private bool _renderWorkerStopping;
    private bool _disposed;

    public Direct3D12AudioGraphHost()
    {
        _renderThread = new Thread(RenderWorkerLoop)
        {
            IsBackground = true,
            Name = "Jericho Down DX12 audio graph"
        };
        _renderThread.Start();
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsReady => _renderer is not null;

    public string DeviceDescription => _renderer?.DeviceDescription ?? "DX12 audio graph not initialized";

    public Direct3D12AudioGraphMode GraphMode
    {
        get => (Direct3D12AudioGraphMode)System.Threading.Volatile.Read(ref _graphMode);
        set => System.Threading.Volatile.Write(ref _graphMode, (int)value);
    }

    public int WaterfallLinesPerGap
    {
        get => System.Threading.Volatile.Read(ref _waterfallLinesPerGap);
        set => System.Threading.Volatile.Write(
            ref _waterfallLinesPerGap,
            Math.Clamp(value, MinimumWaterfallLinesPerGap, MaximumWaterfallLinesPerGap));
    }

    public void SetSpectrumHoverRange(double startPosition, double endPosition)
    {
        var start = (float)Math.Clamp(Math.Min(startPosition, endPosition), 0d, 1d);
        var end = (float)Math.Clamp(Math.Max(startPosition, endPosition), 0d, 1d);
        lock (_spectrumHoverLock)
        {
            _spectrumHoverStart = start;
            _spectrumHoverEnd = end;
        }
    }

    public void ClearSpectrumHoverRange()
    {
        lock (_spectrumHoverLock)
        {
            _spectrumHoverStart = float.NaN;
            _spectrumHoverEnd = float.NaN;
        }
    }

    public void AcceptFrame(SpectrumFrame frame)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_renderWorkerLock)
            {
                if (_disposed || _renderWorkerStopping)
                {
                    return;
                }

                _pendingFrame = frame;
            }

            _renderFrameReady.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void ClearFrame()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_renderWorkerLock)
            {
                if (_disposed || _renderWorkerStopping)
                {
                    return;
                }

                _pendingFrame = null;
            }

            lock (_rendererLock)
            {
                if (_disposed)
                {
                    return;
                }

                _renderer?.ClearHistory();
                _renderer?.RenderProofFrame(GraphMode, WaterfallLinesPerGap);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 audio graph clear failed: {ex.Message}");
        }
    }

    private void RenderWorkerLoop()
    {
        try
        {
            while (true)
            {
                _renderFrameReady.WaitOne();

                while (true)
                {
                    SpectrumFrame? frame;
                    lock (_renderWorkerLock)
                    {
                        if (_renderWorkerStopping)
                        {
                            return;
                        }

                        frame = _pendingFrame;
                        _pendingFrame = null;
                    }

                    if (frame is null)
                    {
                        break;
                    }

                    try
                    {
                        float spectrumHoverStart;
                        float spectrumHoverEnd;
                        lock (_spectrumHoverLock)
                        {
                            spectrumHoverStart = _spectrumHoverStart;
                            spectrumHoverEnd = _spectrumHoverEnd;
                        }

                        lock (_rendererLock)
                        {
                            _renderer?.RenderFrame(frame, GraphMode, WaterfallLinesPerGap, spectrumHoverStart, spectrumHoverEnd);
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke(this, $"DX12 audio graph render failed: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            DisposeRenderer();
            _renderFrameReady.Dispose();
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(
            0,
            "static",
            string.Empty,
            WsChild | WsVisible | WsClipChildren | WsClipSiblings | SsBlackRect,
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
            throw new InvalidOperationException("Could not create DX12 audio graph child window.");
        }

        try
        {
            lock (_rendererLock)
            {
                _renderer = new Direct3D12AudioGraphRenderer(
                    _hwnd,
                    Math.Max(1, (int)ActualWidth),
                    Math.Max(1, (int)ActualHeight));
                _renderer.RenderProofFrame(GraphMode, WaterfallLinesPerGap);
            }

            StatusChanged?.Invoke(this, $"{_renderer.DeviceDescription} audio graph ready. {_renderer.StartupStatus}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 audio graph unavailable: {ex.Message}");
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
            lock (_rendererLock)
            {
                _renderer?.Resize(width, height, GraphMode, WaterfallLinesPerGap);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DX12 audio graph resize failed: {ex.Message}");
        }
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_renderWorkerLock)
        {
            _renderWorkerStopping = true;
            _pendingFrame = null;
        }

        _renderFrameReady.Set();
        if (_renderThread is { IsAlive: true } thread && !thread.Join(TimeSpan.FromMilliseconds(350)))
        {
            StatusChanged?.Invoke(this, "DX12 audio graph stop is waiting for the render worker.");
        }

        _renderThread = null;
        base.Dispose();
    }

    private void DisposeRenderer()
    {
        lock (_rendererLock)
        {
            _renderer?.Dispose();
            _renderer = null;
        }
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

    private sealed class Direct3D12AudioGraphRenderer : IDisposable
    {
        private const int FrameCount = 3;
        private const int HistoryDepth = 64;
        private const int PointCount = 384;
        private const int DetailedSpectrumPointCount = 4096;
        private const double DetailedSpectrumSmoothingAmount = 0.09d;
        private const int SegmentLayers = 2;
        private const int MaximumInterpolatedRowsPerGap = MaximumWaterfallLinesPerGap;
        private const int MaximumMicrophoneSpectrumLines = 10;
        private const int MaxRenderedWaveRows = HistoryDepth + (HistoryDepth - 1) * MaximumInterpolatedRowsPerGap;
        private const int GridLineCount = 21;
        private const int MaxGraphSegments = MaxRenderedWaveRows * (PointCount - 1) * SegmentLayers;
        private const int MaxSegments = MaxGraphSegments + GridLineCount;
        private const int SegmentStride = 24;
        private const int UploadBufferBytes = MaxSegments * SegmentStride;

        private readonly ID3D12Device _device;
        private readonly ID3D12CommandQueue _commandQueue;
        private readonly IDXGIFactory4 _factory;
        private readonly ID3D12GraphicsCommandList _commandList;
        private readonly ID3D12Fence _fence;
        private readonly AutoResetEvent _fenceEvent = new(false);
        private readonly ID3D12DescriptorHeap _rtvHeap;
        private readonly int _rtvDescriptorSize;
        private readonly ID3D12Resource?[] _renderTargets = new ID3D12Resource?[FrameCount];
        private readonly FrameResource[] _frameResources = new FrameResource[FrameCount];
        private readonly float[] _history = new float[HistoryDepth * PointCount];
        private readonly float[] _sliceScratch = new float[PointCount];
        private readonly float[] _detailedProcessedTarget = new float[DetailedSpectrumPointCount];
        private readonly float[] _detailedProcessedTrace = new float[DetailedSpectrumPointCount];
        private readonly float[] _detailedSmoothingScratch = new float[DetailedSpectrumPointCount];
        private readonly float[] _detailedReferenceTarget = new float[DetailedSpectrumPointCount];
        private readonly float[] _detailedReferenceTrace = new float[DetailedSpectrumPointCount];
        private readonly float[] _detailedReferenceSmoothingScratch = new float[DetailedSpectrumPointCount];
        private IDXGISwapChain3 _swapChain;
        private ID3D12RootSignature? _rootSignature;
        private ID3D12PipelineState? _linePipelineState;
        private string? _pipelineFailureReason;
        private ulong _fenceValue;
        private int _width;
        private int _height;
        private int _historyStart;
        private int _historyCount;
        private float _detailedVisualCeiling = 0.25f;
        private bool _hasDetailedSpectrumTrace;
        private bool _hasDetailedReferenceTrace;
        private bool _disposed;

        public Direct3D12AudioGraphRenderer(IntPtr hwnd, int width, int height)
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
            _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            CreateRenderTargetViews();

            for (var i = 0; i < FrameCount; i++)
            {
                _frameResources[i] = new FrameResource(
                    _device.CreateCommandAllocator<ID3D12CommandAllocator>(CommandListType.Direct),
                    _device,
                    UploadBufferBytes);
            }

            _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
                0,
                CommandListType.Direct,
                _frameResources[0].CommandAllocator,
                null);
            _commandList.Close();
            _fence = _device.CreateFence<ID3D12Fence>(0);
            TryCreateLinePipeline();
        }

        public string DeviceDescription => "Direct3D 12 reusable audio graph";

        public string StartupStatus => _linePipelineState is null
            ? $"Graph shader unavailable: {_pipelineFailureReason ?? "unknown pipeline failure"}"
            : "Graph shader online.";

        public void ClearHistory()
        {
            Array.Clear(_history);
            Array.Clear(_sliceScratch);
            Array.Clear(_detailedProcessedTarget);
            Array.Clear(_detailedProcessedTrace);
            Array.Clear(_detailedSmoothingScratch);
            Array.Clear(_detailedReferenceTarget);
            Array.Clear(_detailedReferenceTrace);
            Array.Clear(_detailedReferenceSmoothingScratch);
            _historyStart = 0;
            _historyCount = 0;
            _detailedVisualCeiling = 0.25f;
            _hasDetailedSpectrumTrace = false;
            _hasDetailedReferenceTrace = false;
        }

        public unsafe void RenderFrame(
            SpectrumFrame frame,
            Direct3D12AudioGraphMode graphMode,
            int waterfallLinesPerGap,
            float spectrumHoverStart,
            float spectrumHoverEnd)
        {
            if (_disposed)
            {
                return;
            }

            var frameResource = BeginFrame(out var frameIndex);
            var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 audio graph render target is not ready.");
            var segmentCount = graphMode switch
            {
                Direct3D12AudioGraphMode.SelectedMicSpectrum => WriteSelectedMicSpectrumSegments(frameResource.UploadPointer, MaxSegments, frame, spectrumHoverStart, spectrumHoverEnd),
                Direct3D12AudioGraphMode.ProgramOutputSpectrum => WriteProgramOutputSpectrumSegments(frameResource.UploadPointer, MaxSegments, frame),
                Direct3D12AudioGraphMode.MicrophoneSpectrumLines => WriteMicrophoneSpectrumSegments(frameResource.UploadPointer, MaxSegments, frame),
                _ => WriteWaterfallFrameSegments(frameResource.UploadPointer, MaxSegments, frame, waterfallLinesPerGap)
            };
            DrawSegments(frameResource, renderTarget, frameIndex, segmentCount);
        }

        public unsafe void RenderProofFrame(
            Direct3D12AudioGraphMode graphMode = Direct3D12AudioGraphMode.Waterfall,
            int waterfallLinesPerGap = DefaultWaterfallLinesPerGap)
        {
            if (_disposed)
            {
                return;
            }

            var frameResource = BeginFrame(out var frameIndex);
            var renderTarget = _renderTargets[frameIndex] ?? throw new InvalidOperationException("DX12 audio graph render target is not ready.");
            var segmentCount = WriteProofSegments(frameResource.UploadPointer, MaxSegments, graphMode, waterfallLinesPerGap);
            DrawSegments(frameResource, renderTarget, frameIndex, segmentCount);
        }

        public void Resize(int width, int height, Direct3D12AudioGraphMode graphMode, int waterfallLinesPerGap)
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
            RenderProofFrame(graphMode, waterfallLinesPerGap);
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
            _linePipelineState?.Dispose();
            _rootSignature?.Dispose();
            _rtvHeap.Dispose();
            _swapChain.Dispose();
            _fence.Dispose();
            _fenceEvent.Dispose();
            _commandList.Dispose();
            foreach (var frameResource in _frameResources)
            {
                frameResource.Dispose();
            }

            _factory.Dispose();
            _commandQueue.Dispose();
            _device.Dispose();
        }

        private unsafe int WriteWaterfallFrameSegments(
            IntPtr destination,
            int maxSegments,
            SpectrumFrame frame,
            int waterfallLinesPerGap)
        {
            BuildSlice(frame, _sliceScratch);
            PushHistorySlice(_sliceScratch);
            return WriteGraphSegments(destination, maxSegments, waterfallLinesPerGap);
        }

        private unsafe int WriteProofSegments(
            IntPtr destination,
            int maxSegments,
            Direct3D12AudioGraphMode graphMode,
            int waterfallLinesPerGap)
        {
            var segments = (GraphSegment*)destination;
            return graphMode switch
            {
                Direct3D12AudioGraphMode.SelectedMicSpectrum => WriteDetailedSpectrumGridSegments(segments, maxSegments, float.NaN, float.NaN),
                Direct3D12AudioGraphMode.ProgramOutputSpectrum => WriteDetailedSpectrumGridSegments(segments, maxSegments, float.NaN, float.NaN),
                Direct3D12AudioGraphMode.MicrophoneSpectrumLines => WriteFlatSpectrumGridSegments(segments, maxSegments),
                _ => WriteGraphSegments(destination, maxSegments, waterfallLinesPerGap)
            };
        }

        private unsafe int WriteSelectedMicSpectrumSegments(
            IntPtr destination,
            int maxSegments,
            SpectrumFrame frame,
            float spectrumHoverStart,
            float spectrumHoverEnd)
        {
            var segments = (GraphSegment*)destination;
            var count = WriteDetailedSpectrumGridSegments(segments, maxSegments, spectrumHoverStart, spectrumHoverEnd);
            PrepareDetailedSpectrumTrace(frame);
            count = WriteDetailedSpectrumLineSegments(segments, count, maxSegments, _detailedProcessedTrace, processed: true);
            return count;
        }

        private unsafe int WriteProgramOutputSpectrumSegments(
            IntPtr destination,
            int maxSegments,
            SpectrumFrame frame)
        {
            var segments = (GraphSegment*)destination;
            var count = WriteDetailedSpectrumGridSegments(segments, maxSegments, float.NaN, float.NaN);
            PrepareDetailedSpectrumTraces(frame);
            if (frame.RawMagnitudes.Length > 0)
            {
                count = WriteDetailedSpectrumLineSegments(segments, count, maxSegments, _detailedReferenceTrace, processed: false);
            }

            count = WriteDetailedSpectrumLineSegments(segments, count, maxSegments, _detailedProcessedTrace, processed: true);
            return count;
        }

        private unsafe int WriteMicrophoneSpectrumSegments(IntPtr destination, int maxSegments, SpectrumFrame frame)
        {
            var segments = (GraphSegment*)destination;
            var count = WriteFlatSpectrumGridSegments(segments, maxSegments);
            var renderedLineCount = 0;
            if (frame.MicrophoneLines.Count == 0)
            {
                return WriteMicrophoneLineSegments(segments, count, maxSegments, 0, frame.Magnitudes);
            }

            foreach (var line in frame.MicrophoneLines.OrderBy(line => line.ChannelNumber))
            {
                if (renderedLineCount >= MaximumMicrophoneSpectrumLines)
                {
                    break;
                }

                if (line.Magnitudes.Length == 0)
                {
                    continue;
                }

                count = WriteMicrophoneLineSegments(
                    segments,
                    count,
                    maxSegments,
                    Math.Clamp(line.ChannelNumber - 1, 0, MaximumMicrophoneSpectrumLines - 1),
                    line.Magnitudes);
                renderedLineCount++;
            }

            return count;
        }

        private unsafe int WriteMicrophoneLineSegments(
            GraphSegment* segments,
            int count,
            int maxSegments,
            int lineIndex,
            double[] magnitudes)
        {
            if (magnitudes.Length == 0)
            {
                return count;
            }

            const float xMin = -0.96f;
            const float xMax = 0.96f;
            const float yBottom = -0.80f;
            const float yTop = 0.82f;
            BuildSpectrumSlice(magnitudes, _sliceScratch);
            for (var point = 0; point < PointCount - 1 && count + SegmentLayers <= maxSegments; point++)
            {
                var xA = Lerp(xMin, xMax, point / (double)(PointCount - 1));
                var xB = Lerp(xMin, xMax, (point + 1) / (double)(PointCount - 1));
                var yA = Lerp(yBottom, yTop, Math.Clamp(_sliceScratch[point], 0f, 1f));
                var yB = Lerp(yBottom, yTop, Math.Clamp(_sliceScratch[point + 1], 0f, 1f));
                var layer = 4f + lineIndex;
                segments[count++] = new GraphSegment(xA, yA, xB, yB, layer, 0f);
                segments[count++] = new GraphSegment(xA, yA, xB, yB, layer, 1f);
            }

            return count;
        }

        private static unsafe int WriteFlatSpectrumGridSegments(GraphSegment* segments, int maxSegments)
        {
            const int horizontalLineCount = 5;
            const int verticalLineCount = 9;
            const float xMin = -0.96f;
            const float xMax = 0.96f;
            const float yBottom = -0.80f;
            const float yTop = 0.82f;
            var count = 0;

            for (var i = 0; i < horizontalLineCount && count < maxSegments; i++)
            {
                var y = Lerp(yBottom, yTop, i / (double)(horizontalLineCount - 1));
                var layer = i == 0 ? 22f : 21f;
                segments[count++] = new GraphSegment(xMin, y, xMax, y, layer);
            }

            for (var i = 0; i < verticalLineCount && count < maxSegments; i++)
            {
                var x = Lerp(xMin, xMax, i / (double)(verticalLineCount - 1));
                var layer = i == 0 || i == verticalLineCount - 1 ? 22f : 21f;
                segments[count++] = new GraphSegment(x, yBottom, x, yTop, layer);
            }

            return count;
        }

        private static unsafe int WriteDetailedSpectrumGridSegments(
            GraphSegment* segments,
            int maxSegments,
            float spectrumHoverStart,
            float spectrumHoverEnd)
        {
            const int horizontalLineCount = 9;
            const int verticalLineCount = 17;
            const float xMin = -0.97f;
            const float xMax = 0.97f;
            const float yBottom = -0.82f;
            const float yTop = 0.84f;
            var count = 0;
            if (float.IsFinite(spectrumHoverStart)
                && float.IsFinite(spectrumHoverEnd)
                && spectrumHoverEnd - spectrumHoverStart > 0.001f
                && count + 3 <= maxSegments)
            {
                var left = Lerp(xMin, xMax, spectrumHoverStart);
                var right = Lerp(xMin, xMax, spectrumHoverEnd);
                var center = (yBottom + yTop) * 0.5f;
                segments[count++] = new GraphSegment(left, center, right, center, 35f);
                segments[count++] = new GraphSegment(left, yBottom, left, yTop, 36f);
                segments[count++] = new GraphSegment(right, yBottom, right, yTop, 36f);
            }

            for (var i = 0; i < horizontalLineCount && count < maxSegments; i++)
            {
                var y = Lerp(yBottom, yTop, i / (double)(horizontalLineCount - 1));
                var layer = i == 0 || i == horizontalLineCount - 1 || i == horizontalLineCount / 2 ? 32f : 31f;
                segments[count++] = new GraphSegment(xMin, y, xMax, y, layer);
            }

            for (var i = 0; i < verticalLineCount && count < maxSegments; i++)
            {
                var x = Lerp(xMin, xMax, i / (double)(verticalLineCount - 1));
                var layer = i == 0 || i == verticalLineCount - 1 || i == verticalLineCount / 2 ? 32f : 31f;
                segments[count++] = new GraphSegment(x, yBottom, x, yTop, layer);
            }

            return count;
        }

        private unsafe int WriteDetailedSpectrumLineSegments(
            GraphSegment* segments,
            int count,
            int maxSegments,
            float[] trace,
            bool processed)
        {
            if (trace.Length == 0)
            {
                return count;
            }

            const float xMin = -0.97f;
            const float xMax = 0.97f;
            const float yBottom = -0.82f;
            const float yTop = 0.84f;
            var layer = processed ? 41f : 40f;
            var pointCount = Math.Min(trace.Length, DetailedSpectrumPointCount);
            for (var point = 0; point < pointCount - 1 && count + 2 <= maxSegments; point++)
            {
                var xA = Lerp(xMin, xMax, point / (double)(pointCount - 1));
                var xB = Lerp(xMin, xMax, (point + 1) / (double)(pointCount - 1));
                var yA = Lerp(yBottom, yTop, Math.Clamp(trace[point], 0f, 1f));
                var yB = Lerp(yBottom, yTop, Math.Clamp(trace[point + 1], 0f, 1f));
                segments[count++] = new GraphSegment(xA, yA, xB, yB, layer, 1f);
                segments[count++] = new GraphSegment(xA, yA, xB, yB, layer, 2f);
            }

            return count;
        }

        private unsafe int WriteGraphSegments(IntPtr destination, int maxSegments, int waterfallLinesPerGap = DefaultWaterfallLinesPerGap)
        {
            var segments = (GraphSegment*)destination;
            var count = WriteGridSegments(segments, maxSegments);
            var rows = Math.Min(_historyCount, HistoryDepth);
            var interpolatedRowsPerGap = Math.Clamp(
                waterfallLinesPerGap,
                MinimumWaterfallLinesPerGap,
                MaximumWaterfallLinesPerGap);
            var renderedRows = rows <= 1
                ? rows
                : rows + (rows - 1) * interpolatedRowsPerGap;
            for (var renderedRow = 0; renderedRow < renderedRows && count + (PointCount - 1) * SegmentLayers <= maxSegments; renderedRow++)
            {
                var age = renderedRows <= 1
                    ? 1f
                    : renderedRow / (float)(renderedRows - 1);
                var sourcePosition = age * Math.Max(0, rows - 1);
                var rowA = Math.Min(rows - 1, (int)Math.Floor(sourcePosition));
                var rowB = Math.Min(rows - 1, rowA + 1);
                var blend = sourcePosition - rowA;
                var sourceIndexA = GetHistoryIndex(rowA);
                var sourceIndexB = GetHistoryIndex(rowB);
                for (var point = 0; point < PointCount - 1; point++)
                {
                    var a = Lerp(_history[sourceIndexA + point], _history[sourceIndexB + point], blend);
                    var b = Lerp(_history[sourceIndexA + point + 1], _history[sourceIndexB + point + 1], blend);
                    segments[count++] = new GraphSegment(point, age, a, b, 0f);
                    segments[count++] = new GraphSegment(point, age, a, b, 1f);
                }
            }

            return count;
        }

        private static float Lerp(float a, float b, double amount)
        {
            return a + (b - a) * (float)amount;
        }

        private static unsafe int WriteGridSegments(GraphSegment* segments, int maxSegments)
        {
            var count = 0;
            Span<float> depthLines = stackalloc float[] { 0.00f, 0.12f, 0.24f, 0.36f, 0.48f, 0.60f, 0.72f, 0.84f, 0.96f, 1.00f };
            foreach (var frontness in depthLines)
            {
                if (count >= maxSegments)
                {
                    return count;
                }

                var layer = frontness >= 0.99f ? 3f : 2f;
                segments[count++] = new GraphSegment(-1f, frontness, 1f, frontness, layer);
            }

            Span<float> verticalLines = stackalloc float[] { -0.90f, -0.675f, -0.45f, -0.225f, 0f, 0.225f, 0.45f, 0.675f, 0.90f };
            foreach (var x in verticalLines)
            {
                if (count >= maxSegments)
                {
                    return count;
                }

                var layer = Math.Abs(x) < 0.001f ? 3f : 2f;
                segments[count++] = new GraphSegment(x, 0f, x, 1f, layer);
            }

            if (count < maxSegments)
            {
                segments[count++] = new GraphSegment(-1f, 0f, -1f, 1f, 3f);
            }

            if (count < maxSegments)
            {
                segments[count++] = new GraphSegment(1f, 0f, 1f, 1f, 3f);
            }

            return count;
        }

        private int GetHistoryIndex(int row)
        {
            var oldest = _historyCount == HistoryDepth
                ? _historyStart
                : 0;
            return ((oldest + row) % HistoryDepth) * PointCount;
        }

        private void PushHistorySlice(float[] slice)
        {
            var row = _historyCount < HistoryDepth
                ? _historyCount++
                : _historyStart;
            Array.Copy(slice, 0, _history, row * PointCount, PointCount);
            if (_historyCount == HistoryDepth)
            {
                _historyStart = (_historyStart + 1) % HistoryDepth;
            }
        }

        private static void BuildSlice(SpectrumFrame frame, float[] destination)
        {
            if (frame.Magnitudes.Length > 0)
            {
                BuildSpectrumSlice(frame.Magnitudes, destination);
                return;
            }

            var samples = frame.ProcessedSamples.Length > 0
                ? frame.ProcessedSamples
                : frame.RawSamples;
            if (samples.Length > 0)
            {
                BuildWaveformSlice(samples, destination);
                return;
            }

            BuildSpectrumSlice(frame.Magnitudes, destination);
        }

        private static void BuildWaveformSlice(float[] samples, float[] destination)
        {
            if (samples.Length == 0)
            {
                Array.Clear(destination);
                return;
            }

            for (var point = 0; point < destination.Length; point++)
            {
                var start = point * samples.Length / destination.Length;
                var end = Math.Max(start + 1, (point + 1) * samples.Length / destination.Length);
                end = Math.Min(end, samples.Length);
                var maxAbs = 0f;
                var signedPeak = 0f;
                for (var i = start; i < end; i++)
                {
                    var sample = Math.Clamp(samples[i], -1f, 1f);
                    var abs = Math.Abs(sample);
                    if (abs > maxAbs)
                    {
                        maxAbs = abs;
                        signedPeak = sample;
                    }
                }

                destination[point] = MathF.Tanh(signedPeak * 2.0f);
            }

            SmoothSlice(destination);
        }

        private static void SmoothSlice(float[] destination)
        {
            if (destination.Length < 3)
            {
                return;
            }

            for (var pass = 0; pass < 2; pass++)
            {
                var previous = destination[0];
                for (var i = 1; i < destination.Length - 1; i++)
                {
                    var current = destination[i];
                    var next = destination[i + 1];
                    destination[i] = previous * 0.18f + current * 0.64f + next * 0.18f;
                    previous = current;
                }
            }
        }

        private static void BuildSpectrumSlice(double[] magnitudes, float[] destination)
        {
            if (magnitudes.Length == 0)
            {
                Array.Clear(destination);
                return;
            }

            for (var point = 0; point < destination.Length; point++)
            {
                var sourcePosition = point / (double)Math.Max(1, destination.Length - 1) * (magnitudes.Length - 1);
                var sourceIndexA = Math.Clamp((int)Math.Floor(sourcePosition), 0, magnitudes.Length - 1);
                var sourceIndexB = Math.Clamp(sourceIndexA + 1, 0, magnitudes.Length - 1);
                var blend = sourcePosition - sourceIndexA;
                var magnitude = (double)Lerp((float)magnitudes[sourceIndexA], (float)magnitudes[sourceIndexB], blend);
                destination[point] = (float)ShapeDetailedMagnitude(magnitude);
            }

            SmoothSlice(destination);
        }

        private static void BuildDetailedSpectrumSlice(double[] magnitudes, float[] destination)
        {
            if (magnitudes.Length == 0)
            {
                Array.Clear(destination);
                return;
            }

            for (var point = 0; point < destination.Length; point++)
            {
                var sourcePosition = point / (double)Math.Max(1, destination.Length - 1) * (magnitudes.Length - 1);
                var sourceIndexA = Math.Clamp((int)Math.Floor(sourcePosition), 0, magnitudes.Length - 1);
                var sourceIndexB = Math.Clamp(sourceIndexA + 1, 0, magnitudes.Length - 1);
                var blend = sourcePosition - sourceIndexA;
                var magnitude = (double)Lerp((float)magnitudes[sourceIndexA], (float)magnitudes[sourceIndexB], blend);
                destination[point] = (float)Math.Clamp(Math.Pow(Math.Clamp(magnitude, 0d, 1d), 0.82d) * 0.82d, 0d, 0.94d);
            }

            SmoothSlice(destination);
        }

        private void PrepareDetailedSpectrumTrace(SpectrumFrame frame)
        {
            BuildDetailedSpectrumTarget(frame.Magnitudes, _detailedProcessedTarget, _detailedSmoothingScratch);

            var frameCeiling = 0.08f;
            for (var i = 0; i < DetailedSpectrumPointCount; i++)
            {
                frameCeiling = Math.Max(frameCeiling, _detailedProcessedTarget[i]);
            }

            _detailedVisualCeiling = frameCeiling > _detailedVisualCeiling
                ? Lerp(_detailedVisualCeiling, frameCeiling, 0.08d)
                : Lerp(_detailedVisualCeiling, frameCeiling, 0.015d);

            NormalizeDetailedSpectrumTarget(_detailedProcessedTarget, _detailedVisualCeiling);
            EaseDetailedSpectrumTrace(_detailedProcessedTarget, _detailedProcessedTrace);
            _hasDetailedSpectrumTrace = true;
        }

        private void PrepareDetailedSpectrumTraces(SpectrumFrame frame)
        {
            BuildDetailedSpectrumTarget(frame.Magnitudes, _detailedProcessedTarget, _detailedSmoothingScratch);
            BuildDetailedSpectrumTarget(frame.RawMagnitudes, _detailedReferenceTarget, _detailedReferenceSmoothingScratch);

            var frameCeiling = 0.08f;
            for (var i = 0; i < DetailedSpectrumPointCount; i++)
            {
                frameCeiling = Math.Max(frameCeiling, _detailedProcessedTarget[i]);
                frameCeiling = Math.Max(frameCeiling, _detailedReferenceTarget[i]);
            }

            _detailedVisualCeiling = frameCeiling > _detailedVisualCeiling
                ? Lerp(_detailedVisualCeiling, frameCeiling, 0.08d)
                : Lerp(_detailedVisualCeiling, frameCeiling, 0.015d);

            NormalizeDetailedSpectrumTarget(_detailedProcessedTarget, _detailedVisualCeiling);
            NormalizeDetailedSpectrumTarget(_detailedReferenceTarget, _detailedVisualCeiling);
            EaseDetailedSpectrumTrace(_detailedProcessedTarget, _detailedProcessedTrace, ref _hasDetailedSpectrumTrace);
            EaseDetailedSpectrumTrace(_detailedReferenceTarget, _detailedReferenceTrace, ref _hasDetailedReferenceTrace);
        }

        private static void BuildDetailedSpectrumTarget(double[] magnitudes, float[] destination, float[] smoothingScratch)
        {
            if (magnitudes.Length == 0)
            {
                Array.Clear(destination);
                return;
            }

            for (var point = 0; point < destination.Length; point++)
            {
                var sourcePosition = point / (double)Math.Max(1, destination.Length - 1) * (magnitudes.Length - 1);
                var sourceIndex = (int)Math.Floor(sourcePosition);
                var blend = sourcePosition - sourceIndex;
                var p0 = ShapeDetailedMagnitude(magnitudes[Math.Clamp(sourceIndex - 1, 0, magnitudes.Length - 1)]);
                var p1 = ShapeDetailedMagnitude(magnitudes[Math.Clamp(sourceIndex, 0, magnitudes.Length - 1)]);
                var p2 = ShapeDetailedMagnitude(magnitudes[Math.Clamp(sourceIndex + 1, 0, magnitudes.Length - 1)]);
                var p3 = ShapeDetailedMagnitude(magnitudes[Math.Clamp(sourceIndex + 2, 0, magnitudes.Length - 1)]);
                destination[point] = (float)Math.Clamp(CatmullRom(p0, p1, p2, p3, blend), 0d, 1d);
            }

            SmoothDetailedSpectrumFrequencySteps(destination, smoothingScratch);
        }

        private static void SmoothDetailedSpectrumFrequencySteps(float[] destination, float[] scratch)
        {
            if (destination.Length < 5 || scratch.Length < destination.Length)
            {
                return;
            }

            Array.Copy(destination, scratch, destination.Length);
            var lowEndTaperLength = Math.Max(1f, destination.Length * 0.28f);
            for (var i = 1; i < destination.Length - 1; i++)
            {
                var p0 = scratch[Math.Max(0, i - 2)];
                var p1 = scratch[i - 1];
                var p2 = scratch[i];
                var p3 = scratch[i + 1];
                var p4 = scratch[Math.Min(destination.Length - 1, i + 2)];
                var rounded = p0 * 0.0625f + p1 * 0.25f + p2 * 0.375f + p3 * 0.25f + p4 * 0.0625f;
                var lowEndAmount = Math.Clamp(1f - i / lowEndTaperLength, 0f, 1f);
                var blend = 0.08f + lowEndAmount * 0.34f;
                destination[i] = Lerp(p2, rounded, blend);
            }
        }

        private static double ShapeDetailedMagnitude(double magnitude)
        {
            var lifted = Math.Max(0d, magnitude - 0.01d);
            return Math.Clamp(Math.Pow(lifted, 0.9d), 0d, 1d);
        }

        private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5d * ((2d * p1)
                + (-p0 + p2) * t
                + (2d * p0 - 5d * p1 + 4d * p2 - p3) * t2
                + (-p0 + 3d * p1 - 3d * p2 + p3) * t3);
        }

        private static void NormalizeDetailedSpectrumTarget(float[] target, float visualCeiling)
        {
            var ceiling = Math.Max(0.08f, visualCeiling);
            for (var i = 0; i < target.Length; i++)
            {
                target[i] = Math.Clamp(target[i] / ceiling * 0.62f, 0f, 0.88f);
            }
        }

        private void EaseDetailedSpectrumTrace(float[] target, float[] trace)
        {
            EaseDetailedSpectrumTrace(target, trace, ref _hasDetailedSpectrumTrace);
        }

        private static void EaseDetailedSpectrumTrace(float[] target, float[] trace, ref bool hasTrace)
        {
            if (!hasTrace)
            {
                Array.Copy(target, trace, target.Length);
                hasTrace = true;
                return;
            }

            for (var i = 0; i < target.Length; i++)
            {
                trace[i] = Lerp(trace[i], target[i], DetailedSpectrumSmoothingAmount);
            }
        }

        private void DrawSegments(FrameResource frameResource, ID3D12Resource renderTarget, int frameIndex, int segmentCount)
        {
            var toRenderTarget = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.Present,
                ResourceStates.RenderTarget);
            _commandList.ResourceBarrier([toRenderTarget]);

            var rtvHandle = GetRtvHandle(frameIndex);
            _commandList.OMSetRenderTargets(rtvHandle, null);
            _commandList.ClearRenderTargetView(rtvHandle, new Color4(0.004f, 0.010f, 0.016f, 1f), 0, null!);
            if (_rootSignature is not null && _linePipelineState is not null && segmentCount > 0)
            {
                _commandList.SetGraphicsRootSignature(_rootSignature);
                _commandList.SetPipelineState(_linePipelineState);
                _commandList.SetGraphicsRoot32BitConstant(
                    0,
                    BitConverter.SingleToUInt32Bits(Math.Max(1f, _width * 0.5f)),
                    0);
                _commandList.SetGraphicsRoot32BitConstant(
                    0,
                    BitConverter.SingleToUInt32Bits(Math.Max(1f, _height * 0.5f)),
                    1);
                _commandList.RSSetViewport(new Viewport(0, 0, _width, _height));
                _commandList.RSSetScissorRect(new Vortice.Mathematics.RectI(0, 0, _width, _height));
                _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _commandList.IASetVertexBuffers(0, [frameResource.VertexBufferView]);
                _commandList.DrawInstanced(6, (uint)segmentCount, 0, 0);
            }

            var toPresent = ResourceBarrier.BarrierTransition(
                renderTarget,
                ResourceStates.RenderTarget,
                ResourceStates.Present);
            _commandList.ResourceBarrier([toPresent]);
            ExecuteAndPresent(frameResource);
        }

        private void TryCreateLinePipeline()
        {
            try
            {
                CreateLinePipeline();
                _pipelineFailureReason = null;
            }
            catch (Exception ex)
            {
                _pipelineFailureReason = ex.Message;
                _linePipelineState?.Dispose();
                _linePipelineState = null;
                _rootSignature?.Dispose();
                _rootSignature = null;
            }
        }

        private void CreateLinePipeline()
        {
            var vertexShader = CompileShader("VSMain", "vs_5_0");
            var pixelShader = CompileShader("PSMain", "ps_5_0");
            var parameters = new[]
            {
                new RootParameter(new RootConstants(0, 0, 2), ShaderVisibility.Vertex)
            };
            var rootDescription = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                parameters,
                Array.Empty<StaticSamplerDescription>());
            _rootSignature = _device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
            var inputElements = new[]
            {
                new InputElementDescription("SEGMENT", 0, Format.R32G32B32A32_Float, 0, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("SEGMENT", 1, Format.R32G32_Float, 16, 0, InputClassification.PerInstanceData, 1)
            };
            var pipelineDescription = new GraphicsPipelineStateDescription
            {
                RootSignature = _rootSignature,
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                BlendState = BlendDescription.AlphaBlend,
                RasterizerState = RasterizerDescription.CullNone,
                DepthStencilState = DepthStencilDescription.None,
                InputLayout = new InputLayoutDescription(inputElements),
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = [Format.B8G8R8A8_UNorm],
                SampleDescription = new SampleDescription(1, 0)
            };
            _linePipelineState = _device.CreateGraphicsPipelineState<ID3D12PipelineState>(pipelineDescription);
        }

        private static byte[] CompileShader(string entryPoint, string profile)
        {
            return Compiler.Compile(
                    AudioGraphShaderSource,
                    entryPoint,
                    "JerichoDownAudioGraph.hlsl",
                    profile,
                    ShaderFlags.OptimizationLevel3,
                    EffectFlags.None)
                .ToArray();
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

        private FrameResource BeginFrame(out int frameIndex)
        {
            frameIndex = (int)_swapChain.CurrentBackBufferIndex;
            var frameResource = _frameResources[frameIndex];
            WaitForFrameResource(frameResource);
            frameResource.CommandAllocator.Reset();
            _commandList.Reset(frameResource.CommandAllocator);
            return frameResource;
        }

        private void ExecuteAndPresent(FrameResource frameResource)
        {
            _commandList.Close();
            _commandQueue.ExecuteCommandList(_commandList);
            _swapChain.Present(0, PresentFlags.None);
            SignalFrameSubmitted(frameResource);
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
                ClearFrameFenceValues();
                return;
            }

            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            _fenceEvent.WaitOne();
            ClearFrameFenceValues();
        }

        private void SignalFrameSubmitted(FrameResource frameResource)
        {
            if (_disposed)
            {
                return;
            }

            _fenceValue++;
            _commandQueue.Signal(_fence, _fenceValue);
            frameResource.FenceValue = _fenceValue;
        }

        private void WaitForFrameResource(FrameResource frameResource)
        {
            if (_disposed || frameResource.FenceValue == 0)
            {
                return;
            }

            if (_fence.CompletedValue < frameResource.FenceValue)
            {
                _fence.SetEventOnCompletion(frameResource.FenceValue, _fenceEvent);
                _fenceEvent.WaitOne();
            }

            frameResource.FenceValue = 0;
        }

        private void ClearFrameFenceValues()
        {
            foreach (var frameResource in _frameResources)
            {
                frameResource.FenceValue = 0;
            }
        }

        // Camera Flash V1 was the all-white failure mode; keep this shader bounded.
        private const string AudioGraphShaderSource = """
            struct VertexInput
            {
                float4 Segment0 : SEGMENT0;
                float2 Segment1 : SEGMENT1;
            };

            struct VertexOutput
            {
                float4 Position : SV_POSITION;
                float4 Color : COLOR;
                float LineSide : TEXCOORD0;
            };

            cbuffer GraphViewport : register(b0)
            {
                float2 ViewportHalfSize;
            };

            float Smooth01(float value)
            {
                value = saturate(value);
                return value * value * (3.0 - 2.0 * value);
            }

            float3 FrequencyColor(float position)
            {
                float t = saturate(position);
                float3 red = float3(1.0, 0.16, 0.02);
                float3 orange = float3(1.0, 0.58, 0.06);
                float3 pink = float3(1.0, 0.08, 0.82);
                float3 violet = float3(0.50, 0.22, 1.0);
                float3 blue = float3(0.05, 0.38, 1.0);
                float3 cyan = float3(0.00, 1.0, 0.96);
                float3 c = lerp(red, orange, smoothstep(0.00, 0.17, t));
                c = lerp(c, pink, smoothstep(0.13, 0.34, t));
                c = lerp(c, violet, smoothstep(0.30, 0.54, t));
                c = lerp(c, blue, smoothstep(0.50, 0.74, t));
                c = lerp(c, cyan, smoothstep(0.70, 1.00, t));
                return saturate(c);
            }

            float3 MicrophoneLineColor(float index)
            {
                if (index < 0.5) return float3(1.00, 0.28, 0.28);
                if (index < 1.5) return float3(0.22, 0.90, 0.42);
                if (index < 2.5) return float3(0.27, 0.67, 1.00);
                if (index < 3.5) return float3(1.00, 0.83, 0.25);
                if (index < 4.5) return float3(0.85, 0.44, 1.00);
                if (index < 5.5) return float3(0.00, 0.88, 1.00);
                if (index < 6.5) return float3(1.00, 0.53, 0.28);
                if (index < 7.5) return float3(0.60, 0.93, 0.38);
                if (index < 8.5) return float3(1.00, 0.44, 0.71);
                return float3(0.91, 0.94, 0.97);
            }

            float2 ProjectFloor(float x, float frontness)
            {
                float front = saturate(frontness);
                float depth = Smooth01(front);
                float xScale = 0.26 + depth * 0.72;
                float xDrift = -0.11 * (1.0 - depth);
                float y = 0.84 - depth * 1.66;
                return float2((x + xDrift) * xScale, y);
            }

            float2 ProjectWave(float pointIndex, float frontness, float value)
            {
                float front = saturate(frontness);
                float depth = Smooth01(front);
                float x = (pointIndex / 383.0) * 2.0 - 1.0;
                float2 floorPoint = ProjectFloor(x, front);
                float edgeFade = saturate(1.0 - pow(abs(x), 2.15) * 0.58);
                float waveHeight = 2.0;
                float amplitude = (0.12 + depth * 0.47) * edgeFade * waveHeight;
                float contourLift = value * value * (0.04 + depth * 0.05) * edgeFade * waveHeight;
                return floorPoint + float2(0.0, value * amplitude + contourLift);
            }

            VertexOutput VSMain(VertexInput input, uint vertexId : SV_VertexID)
            {
                float rawLayer = input.Segment1.x;
                float2 p1;
                float2 p2;
                float thickness;
                float4 color;
                if (rawLayer > 39.5)
                {
                    p1 = float2(clamp(input.Segment0.x, -0.99, 0.99), clamp(input.Segment0.y, -0.92, 0.92));
                    p2 = float2(clamp(input.Segment0.z, -0.99, 0.99), clamp(input.Segment0.w, -0.92, 0.92));
                    float processedLine = rawLayer > 40.5 ? 1.0 : 0.0;
                    float detailLayer = input.Segment1.y;
                    float valueA = saturate((input.Segment0.y + 0.82) / 1.66);
                    float valueB = saturate((input.Segment0.w + 0.82) / 1.66);
                    float loudness = saturate(max(valueA, valueB));
                    float useCore = detailLayer > 1.5 ? 1.0 : 0.0;
                    float useBody = detailLayer > 0.5 ? 1.0 : 0.0;
                    float3 rawColor = float3(0.64, 0.69, 0.75);
                    float3 processedColor = float3(0.00, 0.78, 0.96);
                    float3 traceColor = lerp(rawColor, processedColor, processedLine);
                    thickness = lerp(
                        1.05 + loudness * 0.22,
                        0.52 + loudness * 0.10,
                        useCore);
                    float alpha = lerp(0.22 + loudness * 0.10, 0.82 + loudness * 0.12, useCore);
                    color = float4(saturate(traceColor * (0.72 + useCore * 0.55 + loudness * 0.24)), saturate(alpha));
                }
                else if (rawLayer > 34.5)
                {
                    p1 = float2(clamp(input.Segment0.x, -0.99, 0.99), clamp(input.Segment0.y, -0.92, 0.92));
                    p2 = float2(clamp(input.Segment0.z, -0.99, 0.99), clamp(input.Segment0.w, -0.92, 0.92));
                    float isBorder = rawLayer > 35.5 ? 1.0 : 0.0;
                    thickness = lerp(880.0, 2.2, isBorder);
                    color = lerp(
                        float4(0.00, 0.62, 0.70, 0.050),
                        float4(0.18, 0.82, 0.78, 0.30),
                        isBorder);
                }
                else if (rawLayer > 30.5)
                {
                    p1 = float2(clamp(input.Segment0.x, -0.99, 0.99), clamp(input.Segment0.y, -0.92, 0.92));
                    p2 = float2(clamp(input.Segment0.z, -0.99, 0.99), clamp(input.Segment0.w, -0.92, 0.92));
                    float isMajorLine = rawLayer > 31.5 ? 1.0 : 0.0;
                    thickness = lerp(0.52, 1.05, isMajorLine);
                    color = lerp(
                        float4(0.04, 0.14, 0.18, 0.18),
                        float4(0.08, 0.40, 0.46, 0.34),
                        isMajorLine);
                }
                else if (rawLayer > 20.5)
                {
                    p1 = float2(clamp(input.Segment0.x, -0.98, 0.98), clamp(input.Segment0.y, -0.90, 0.90));
                    p2 = float2(clamp(input.Segment0.z, -0.98, 0.98), clamp(input.Segment0.w, -0.90, 0.90));
                    float isMajorLine = rawLayer > 21.5 ? 1.0 : 0.0;
                    thickness = lerp(0.75, 1.35, isMajorLine);
                    color = lerp(
                        float4(0.05, 0.22, 0.27, 0.22),
                        float4(0.08, 0.68, 0.62, 0.38),
                        isMajorLine);
                }
                else if (rawLayer > 3.5)
                {
                    p1 = float2(clamp(input.Segment0.x, -0.98, 0.98), clamp(input.Segment0.y, -0.90, 0.90));
                    p2 = float2(clamp(input.Segment0.z, -0.98, 0.98), clamp(input.Segment0.w, -0.90, 0.90));
                    float micIndex = floor(rawLayer - 4.0);
                    float useCore = input.Segment1.y > 0.5 ? 1.0 : 0.0;
                    float valueA = saturate((input.Segment0.y + 0.80) / 1.62);
                    float valueB = saturate((input.Segment0.w + 0.80) / 1.62);
                    float loudness = saturate(max(valueA, valueB));
                    float3 micColor = MicrophoneLineColor(micIndex);
                    thickness = lerp(5.8 + loudness * 2.2, 1.55 + loudness * 0.6, useCore);
                    color = lerp(
                        float4(micColor * 0.42, 0.040 + loudness * 0.060),
                        float4(saturate(micColor * 1.35), 0.50 + loudness * 0.32),
                        useCore);
                }
                else if (rawLayer > 1.5)
                {
                    float frontness = saturate(max(input.Segment0.y, input.Segment0.w));
                    p1 = ProjectFloor(clamp(input.Segment0.x, -1.0, 1.0), input.Segment0.y);
                    p2 = ProjectFloor(clamp(input.Segment0.z, -1.0, 1.0), input.Segment0.w);
                    float isCenterLine = rawLayer > 2.5 ? 1.0 : 0.0;
                    float gridCenterFade = saturate(1.0 - pow(max(abs(input.Segment0.x), abs(input.Segment0.z)), 2.1) * 0.24);
                    thickness = lerp(0.65 + frontness * 0.75, 1.4 + frontness * 1.35, isCenterLine);
                    color = lerp(
                        float4(0.010, 0.35 + frontness * 0.15, 0.44 + frontness * 0.18, 0.08 + frontness * 0.13),
                        float4(0.03, 0.82, 0.72, 0.24 + frontness * 0.24),
                        isCenterLine);
                    color.a *= gridCenterFade;
                }
                else
                {
                    float pointIndex = clamp(input.Segment0.x, 0.0, 382.0);
                    float frontness = saturate(input.Segment0.y);
                    float valueA = clamp(input.Segment0.z, -0.85, 0.85);
                    float valueB = clamp(input.Segment0.w, -0.85, 0.85);
                    float layer = saturate(rawLayer);
                    float depth = Smooth01(frontness);
                    p1 = ProjectWave(pointIndex, frontness, valueA);
                    p2 = ProjectWave(pointIndex + 1.0, frontness, valueB);
                    p1 = clamp(p1, float2(-1.05, -0.95), float2(1.05, 0.95));
                    p2 = clamp(p2, float2(-1.05, -0.95), float2(1.05, 0.95));
                    float loudness = saturate(max(abs(valueA), abs(valueB)) * 1.40);
                    float frequencyPosition = pow(pointIndex / 383.0, 0.82);
                    float3 neon = FrequencyColor(frequencyPosition);
                    float xForFade = frequencyPosition * 2.0 - 1.0;
                    float edgeAlpha = saturate(1.0 - pow(abs(xForFade), 2.05) * 0.52);
                    float brightness = 0.18 + depth * 0.48 + loudness * 0.10;
                    float r = saturate(neon.r * brightness);
                    float g = saturate(neon.g * brightness);
                    float b = saturate(neon.b * brightness);
                    float idleFill = 1.0 - loudness;
                    float glowAlpha = (0.010 + depth * 0.026 + idleFill * 0.012 + loudness * 0.010) * edgeAlpha;
                    float coreAlpha = (0.026 + depth * 0.160 + idleFill * 0.020 + loudness * 0.040) * edgeAlpha;
                    float glowPixels = (1.35 + depth * 3.4) * (0.92 + loudness * 0.34);
                    float corePixels = (0.95 + depth * 1.75) * (0.98 + loudness * 0.18);
                    float useCore = layer > 0.5 ? 1.0 : 0.0;
                    thickness = lerp(glowPixels, corePixels, useCore);
                    color = lerp(
                        float4(r * 0.24, g * 0.30, b * 0.34, saturate(glowAlpha)),
                        float4(r, g, b, saturate(coreAlpha)),
                        useCore);
                }

                float2 viewportHalfSize = max(ViewportHalfSize, float2(1.0, 1.0));
                float2 deltaPixels = (p2 - p1) * viewportHalfSize;
                float lengthPixels = max(length(deltaPixels), 0.001);
                float2 normal = float2(-deltaPixels.y, deltaPixels.x) / lengthPixels;
                float halfThickness = max(thickness * 0.5, 0.25);
                float2 offset = normal * halfThickness / viewportHalfSize;
                float2 position = p1 + offset;
                float lineSide = 1.0;
                if (vertexId == 1)
                {
                    position = p1 - offset;
                    lineSide = -1.0;
                }
                else if (vertexId == 2 || vertexId == 3)
                {
                    position = p2 + offset;
                    lineSide = 1.0;
                }
                else if (vertexId == 4)
                {
                    position = p1 - offset;
                    lineSide = -1.0;
                }
                else if (vertexId == 5)
                {
                    position = p2 - offset;
                    lineSide = -1.0;
                }
                position = clamp(position, float2(-1.08, -0.98), float2(1.08, 0.98));

                VertexOutput output;
                output.Position = float4(position, 0.0, 1.0);
                output.Color = color;
                output.LineSide = lineSide;
                return output;
            }

            float4 PSMain(VertexOutput input) : SV_TARGET
            {
                float4 color = input.Color;
                color.a *= 1.0 - smoothstep(0.70, 1.0, abs(input.LineSide));
                return color;
            }
            """;

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct GraphSegment
        {
            public GraphSegment(float point, float age, float sampleA, float sampleB, float layer, float padding = 0f)
            {
                Point = point;
                Age = age;
                SampleA = sampleA;
                SampleB = sampleB;
                Layer = layer;
                Padding = padding;
            }

            public readonly float Point;
            public readonly float Age;
            public readonly float SampleA;
            public readonly float SampleB;
            public readonly float Layer;
            public readonly float Padding;
        }

        private sealed class FrameResource : IDisposable
        {
            public FrameResource(ID3D12CommandAllocator commandAllocator, ID3D12Device device, int uploadBytes)
            {
                CommandAllocator = commandAllocator;
                UploadBuffer = CreateMappedUploadBuffer(device, (ulong)uploadBytes, out var uploadPointer);
                UploadPointer = uploadPointer;
                VertexBufferView = new VertexBufferView(
                    UploadBuffer.GPUVirtualAddress,
                    (uint)uploadBytes,
                    SegmentStride);
            }

            public ID3D12CommandAllocator CommandAllocator { get; }

            public ID3D12Resource UploadBuffer { get; }

            public IntPtr UploadPointer { get; }

            public VertexBufferView VertexBufferView { get; }

            public ulong FenceValue { get; set; }

            public void Dispose()
            {
                UploadBuffer.Unmap(0, null);
                UploadBuffer.Dispose();
                CommandAllocator.Dispose();
            }

            private static unsafe ID3D12Resource CreateMappedUploadBuffer(
                ID3D12Device device,
                ulong uploadBytes,
                out IntPtr mappedPointer)
            {
                var uploadBuffer = device.CreateCommittedResource<ID3D12Resource>(
                    new HeapProperties(HeapType.Upload),
                    HeapFlags.None,
                    ResourceDescription.Buffer(uploadBytes, ResourceFlags.None, 0),
                    ResourceStates.GenericRead);
                void* mapped = null;
                uploadBuffer.Map(0, null, &mapped).CheckError();
                mappedPointer = (IntPtr)mapped;
                return uploadBuffer;
            }
        }
    }
}
