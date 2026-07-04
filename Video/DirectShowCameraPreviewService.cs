using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PodcastWorkbench.Video;

public sealed class DirectShowCameraPreviewService : IDisposable
{
    private static readonly Guid FilterGraphClsid = new("E436EBB3-524F-11CE-9F53-0020AF0BA770");
    private static readonly Guid CaptureGraphBuilder2Clsid = new("BF87B6E1-8C27-11D0-B3F0-00AA003761C5");
    private static readonly Guid SampleGrabberClsid = new("C1F400A0-3F08-11D3-9F0B-006008039E37");
    private static readonly Guid NullRendererClsid = new("C1F400A4-3F08-11D3-9F0B-006008039E37");
    private static readonly Guid SystemDeviceEnumClsid = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    private static readonly Guid PinCategoryCapture = new("FB6C4281-0353-11D1-905F-0000C0CC16BA");
    private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MediaSubtypeRgb32 = new("E436EB7E-524F-11CE-9F53-0020AF0BA770");
    private static readonly Guid FormatVideoInfo = new("05589F80-C356-11CE-BF01-00AA0055595A");

    private readonly object _syncRoot = new();
    private readonly object _recordingLock = new();
    private object? _graphObject;
    private object? _captureBuilderObject;
    private IBaseFilter? _sourceFilter;
    private IBaseFilter? _sampleGrabberFilter;
    private IBaseFilter? _nullRendererFilter;
    private ISampleGrabber? _sampleGrabber;
    private IMediaControl? _mediaControl;
    private CaptureCallback? _callback;
    private int _width;
    private int _height;
    private int _stride;
    private double _framesPerSecond = 30d;
    private bool _bottomUp = true;
    private byte[]? _previousDenoiseFrame;
    private MediaFoundationCameraDeviceFactory.MediaFoundationScope? _recordingMediaFoundationScope;
    private MediaFoundationVideoRecorder? _recorder;
    private string? _recordingPath;
    private bool _recordingStopRequested;
    private ManualResetEventSlim? _recordingStopCompleted;
    private string? _completedRecordingPath;
    private int _completedRecordingSamples;
    private string? _completedRecordingDiagnostics;

    public event EventHandler<CameraFrame>? FrameAvailable;
    public event EventHandler<string>? StatusChanged;

    public bool DenoiseEnabled { get; set; }

    public double DenoiseStrength { get; set; } = 2d;

    public string? LastStatus { get; private set; }

    public string? LastRecordingDiagnostics { get; private set; }

    public bool IsRecording
    {
        get
        {
            lock (_recordingLock)
            {
                return _recorder is not null || !string.IsNullOrWhiteSpace(_recordingPath);
            }
        }
    }

    public bool Start(CameraDevice camera, CameraVideoMode? mode)
    {
        Stop();
        LastStatus = null;
        _framesPerSecond = mode?.FramesPerSecond is > 0d ? mode.FramesPerSecond.Value : 30d;

        if (!OperatingSystem.IsWindows())
        {
            LastStatus = "DirectShow camera preview requires Windows";
            StatusChanged?.Invoke(this, LastStatus);
            return false;
        }

        try
        {
            var graphType = Type.GetTypeFromCLSID(FilterGraphClsid, throwOnError: true)!;
            var builderType = Type.GetTypeFromCLSID(CaptureGraphBuilder2Clsid, throwOnError: true)!;
            var grabberType = Type.GetTypeFromCLSID(SampleGrabberClsid, throwOnError: true)!;
            var rendererType = Type.GetTypeFromCLSID(NullRendererClsid, throwOnError: true)!;

            _graphObject = Activator.CreateInstance(graphType);
            _captureBuilderObject = Activator.CreateInstance(builderType);
            _sampleGrabberFilter = (IBaseFilter)Activator.CreateInstance(grabberType)!;
            _nullRendererFilter = (IBaseFilter)Activator.CreateInstance(rendererType)!;
            _sourceFilter = CreateSourceFilter(camera)
                ?? throw new InvalidOperationException($"DirectShow could not find camera: {camera.Name}");

            var graph = (IGraphBuilder)_graphObject!;
            var captureBuilder = (ICaptureGraphBuilder2)_captureBuilderObject!;
            _sampleGrabber = (ISampleGrabber)_sampleGrabberFilter;
            _mediaControl = (IMediaControl)_graphObject!;

            ThrowIfFailed(captureBuilder.SetFiltergraph(graph), "DirectShow capture graph setup failed");
            ThrowIfFailed(graph.AddFilter(_sourceFilter, "Video Source"), "DirectShow source filter setup failed");
            ThrowIfFailed(graph.AddFilter(_sampleGrabberFilter, "Frame Grabber"), "DirectShow frame grabber setup failed");
            ThrowIfFailed(graph.AddFilter(_nullRendererFilter, "Preview Sink"), "DirectShow preview sink setup failed");

            using var mediaType = new DirectShowMediaType(MediaTypeVideo, MediaSubtypeRgb32, FormatVideoInfo);
            ThrowIfFailed(_sampleGrabber.SetMediaType(mediaType.Value), "DirectShow RGB32 preview format setup failed");
            ThrowIfFailed(_sampleGrabber.SetBufferSamples(false), "DirectShow frame buffering setup failed");
            ThrowIfFailed(_sampleGrabber.SetOneShot(false), "DirectShow frame callback setup failed");

            _callback = new CaptureCallback(this);
            ThrowIfFailed(_sampleGrabber.SetCallback(_callback, 1), "DirectShow callback setup failed");

            var captureCategory = PinCategoryCapture;
            var videoType = MediaTypeVideo;
            ThrowIfFailed(captureBuilder.RenderStream(
                ref captureCategory,
                ref videoType,
                _sourceFilter,
                _sampleGrabberFilter,
                _nullRendererFilter), "DirectShow preview graph render failed");

            ConfigureConnectedFormat();
            ThrowIfFailed(_mediaControl.Run(), "DirectShow preview graph start failed");

            var modeText = mode?.IsAuto == false ? $" at {mode.Label}" : string.Empty;
            LastStatus = $"Starting DirectShow preview: {camera.Name}{modeText}";
            StatusChanged?.Invoke(this, LastStatus);
            return true;
        }
        catch (Exception ex)
        {
            Stop();
            LastStatus = $"Could not start DirectShow preview: {ex.Message}";
            StatusChanged?.Invoke(this, LastStatus);
            return false;
        }
    }

    public bool StartRecording(string path, CameraVideoMode? mode)
    {
        lock (_recordingLock)
        {
            if (_recorder is not null || !string.IsNullOrWhiteSpace(_recordingPath))
            {
                return true;
            }

            int width;
            int height;
            double fps;
            lock (_syncRoot)
            {
                width = _width > 0 ? _width : mode?.Width ?? 1280;
                height = _height > 0 ? _height : mode?.Height ?? 720;
                fps = _framesPerSecond > 0d ? _framesPerSecond : mode?.FramesPerSecond ?? 30d;
            }

            if (width <= 0 || height <= 0)
            {
                LastStatus = "DirectShow recording failed: preview format is not ready.";
                StatusChanged?.Invoke(this, LastStatus);
                return false;
            }

            try
            {
                _recordingMediaFoundationScope ??= MediaFoundationCameraDeviceFactory.Startup();
                _width = width;
                _height = height;
                _framesPerSecond = fps;
                _recordingPath = path;
                _recordingStopRequested = false;
                _completedRecordingPath = null;
                _completedRecordingSamples = 0;
                _completedRecordingDiagnostics = null;
                LastRecordingDiagnostics = null;
                LastStatus = $"Recording DirectShow video: {System.IO.Path.GetFileName(path)}";
                StatusChanged?.Invoke(this, LastStatus);
                return true;
            }
            catch (Exception ex)
            {
                _recorder?.Dispose();
                _recorder = null;
                _recordingPath = null;
                _recordingMediaFoundationScope?.Dispose();
                _recordingMediaFoundationScope = null;
                LastStatus = $"DirectShow video recording failed: {ex.Message}";
                StatusChanged?.Invoke(this, LastStatus);
                return false;
            }
        }
    }

    public void PauseRecording()
    {
        lock (_recordingLock)
        {
            _recorder?.Pause();
        }
    }

    public void ResumeRecording()
    {
        lock (_recordingLock)
        {
            _recorder?.Resume();
        }
    }

    public string? StopRecording()
    {
        ManualResetEventSlim? stopCompleted;
        lock (_recordingLock)
        {
            var recorder = _recorder;
            var path = _recordingPath;
            if (recorder is null && string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (recorder is null)
            {
                _recordingPath = null;
                _recordingStopRequested = false;
                _recordingMediaFoundationScope?.Dispose();
                _recordingMediaFoundationScope = null;
                LastRecordingDiagnostics = "DirectShow recorder: offered 0, wrote 0, skipped 0; no frames reached the recorder.";
                LastStatus = LastRecordingDiagnostics;
                StatusChanged?.Invoke(this, LastRecordingDiagnostics);
                return null;
            }

            _recordingStopRequested = true;
            _completedRecordingPath = null;
            _completedRecordingSamples = 0;
            _completedRecordingDiagnostics = null;
            _recordingStopCompleted?.Dispose();
            _recordingStopCompleted = new ManualResetEventSlim(false);
            stopCompleted = _recordingStopCompleted;
        }

        if (!stopCompleted.Wait(TimeSpan.FromSeconds(3)))
        {
            lock (_recordingLock)
            {
                LastRecordingDiagnostics = "DirectShow recorder: stop timed out waiting for the capture thread to finalize the file.";
                LastStatus = LastRecordingDiagnostics;
                StatusChanged?.Invoke(this, LastRecordingDiagnostics);
                return null;
            }
        }

        lock (_recordingLock)
        {
            LastRecordingDiagnostics = _completedRecordingDiagnostics;
            return _completedRecordingSamples > 0 ? _completedRecordingPath : null;
        }
    }

    private void CompleteRecordingOnCaptureThread()
    {
        var recorder = _recorder;
        var path = _recordingPath;
        _recorder = null;
        _recordingPath = null;
        _recordingStopRequested = false;

        if (recorder is null)
        {
            _completedRecordingPath = null;
            _completedRecordingSamples = 0;
            _completedRecordingDiagnostics = "DirectShow recorder: offered 0, wrote 0, skipped 0; no active writer was available.";
            _recordingStopCompleted?.Set();
            return;
        }

        try
        {
            var samplesWritten = recorder?.SamplesWritten ?? 0;
            var framesOffered = recorder?.FramesOffered ?? 0;
            var framesSkipped = recorder?.FramesSkipped ?? 0;
            var lastSkipReason = recorder?.LastSkipReason;
            recorder?.Stop();
            _completedRecordingPath = path;
            _completedRecordingSamples = samplesWritten;
            _completedRecordingDiagnostics = $"DirectShow recorder: offered {framesOffered}, wrote {samplesWritten}, skipped {framesSkipped}"
                + (string.IsNullOrWhiteSpace(lastSkipReason) ? "." : $"; last skip: {lastSkipReason}.");
        }
        catch (Exception ex)
        {
            _completedRecordingPath = null;
            _completedRecordingSamples = 0;
            _completedRecordingDiagnostics = $"DirectShow recorder finalization failed: {ex.Message}";
        }
        finally
        {
            recorder.Dispose();
            _recordingMediaFoundationScope?.Dispose();
            _recordingMediaFoundationScope = null;
            LastRecordingDiagnostics = _completedRecordingDiagnostics;
            LastStatus = LastRecordingDiagnostics;
            if (!string.IsNullOrWhiteSpace(LastRecordingDiagnostics))
            {
                StatusChanged?.Invoke(this, LastRecordingDiagnostics);
            }

            _recordingStopCompleted?.Set();
        }
    }

    public void Stop()
    {
        StopRecording();

        lock (_syncRoot)
        {
            try
            {
                _mediaControl?.Stop();
            }
            catch
            {
            }

            try
            {
                _sampleGrabber?.SetCallback(null, 0);
            }
            catch
            {
            }

            _callback = null;
            _sampleGrabber = null;
            _mediaControl = null;
            _width = 0;
            _height = 0;
            _stride = 0;
            _framesPerSecond = 30d;
            _previousDenoiseFrame = null;

            ReleaseComObject(_nullRendererFilter);
            ReleaseComObject(_sampleGrabberFilter);
            ReleaseComObject(_sourceFilter);
            ReleaseComObject(_captureBuilderObject);
            ReleaseComObject(_graphObject);

            _nullRendererFilter = null;
            _sampleGrabberFilter = null;
            _sourceFilter = null;
            _captureBuilderObject = null;
            _graphObject = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void ConfigureConnectedFormat()
    {
        if (_sampleGrabber is null)
        {
            return;
        }

        var mediaType = new AMMediaType();
        try
        {
            ThrowIfFailed(_sampleGrabber.GetConnectedMediaType(mediaType), "DirectShow connected media type unavailable");
            if (mediaType.FormatType != FormatVideoInfo || mediaType.FormatPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("DirectShow connected media type is not VideoInfo RGB32.");
            }

            var videoInfo = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.FormatPtr);
            _width = Math.Abs(videoInfo.Bitmap.Width);
            _height = Math.Abs(videoInfo.Bitmap.Height);
            _stride = _width * 4;
            _bottomUp = videoInfo.Bitmap.Height > 0;
            if (videoInfo.AvgTimePerFrame > 0)
            {
                _framesPerSecond = 10_000_000d / videoInfo.AvgTimePerFrame;
            }
        }
        finally
        {
            FreeMediaType(mediaType);
        }
    }

    private void OnBuffer(IntPtr buffer, int length)
    {
        int width;
        int height;
        int stride;
        bool bottomUp;
        lock (_syncRoot)
        {
            width = _width;
            height = _height;
            stride = _stride;
            bottomUp = _bottomUp;
        }

        if (width <= 0 || height <= 0 || stride <= 0 || length < stride * height)
        {
            return;
        }

        var bytes = new byte[stride * height];
        if (bottomUp)
        {
            var row = new byte[stride];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(buffer + ((height - 1 - y) * stride), row, 0, stride);
                Buffer.BlockCopy(row, 0, bytes, y * stride, stride);
            }
        }
        else
        {
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }

        if (DenoiseEnabled)
        {
            ApplyTemporalDenoise(bytes);
        }
        else
        {
            _previousDenoiseFrame = null;
        }

        var frame = new CameraFrame(bytes, width, height, stride);

        lock (_recordingLock)
        {
            try
            {
                if (_recorder is not null)
                {
                    EnsureRecorderMatchesFrame(frame);
                    if (_recorder?.WriteFrame(frame.BgraBytes) == false)
                    {
                        LastStatus = "DirectShow video frame write skipped: frame did not match the active recorder format.";
                        StatusChanged?.Invoke(this, LastStatus);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(_recordingPath))
                {
                    _recordingMediaFoundationScope ??= MediaFoundationCameraDeviceFactory.Startup();
                    _recorder = new MediaFoundationVideoRecorder(
                        _recordingPath,
                        frame.Width,
                        frame.Height,
                        _framesPerSecond,
                        d3dManager: null);
                    _recorder.WriteFrame(frame.BgraBytes);
                }

                if (_recordingStopRequested && _recorder is not null)
                {
                    CompleteRecordingOnCaptureThread();
                }
            }
            catch (Exception ex)
            {
                LastStatus = $"DirectShow video frame write failed: {ex.Message}";
                StatusChanged?.Invoke(this, LastStatus);
                _recorder?.Dispose();
                _recorder = null;
                _recordingPath = null;
            }
        }

        FrameAvailable?.Invoke(this, frame);
    }

    private void EnsureRecorderMatchesFrame(CameraFrame frame)
    {
        if (_recorder is null
            || string.IsNullOrWhiteSpace(_recordingPath)
            || (_recorder.Width == frame.Width && _recorder.Height == frame.Height))
        {
            return;
        }

        _recorder.Dispose();
        _recordingMediaFoundationScope ??= MediaFoundationCameraDeviceFactory.Startup();
        _recorder = new MediaFoundationVideoRecorder(
            _recordingPath,
            frame.Width,
            frame.Height,
            _framesPerSecond,
            d3dManager: null);
        LastStatus = $"DirectShow recorder matched live frame size: {frame.Width}x{frame.Height}.";
        StatusChanged?.Invoke(this, LastStatus);
    }

    private void ApplyTemporalDenoise(byte[] current)
    {
        var previous = _previousDenoiseFrame;
        if (previous is null || previous.Length != current.Length)
        {
            _previousDenoiseFrame = (byte[])current.Clone();
            return;
        }

        var strength = Math.Clamp(DenoiseStrength, 0.5d, 8d);
        var previousWeight = Math.Clamp(strength / 12d, 0.05d, 0.62d);
        var currentWeight = 1d - previousWeight;
        for (var i = 0; i < current.Length; i += 4)
        {
            current[i] = Blend(current[i], previous[i], currentWeight, previousWeight);
            current[i + 1] = Blend(current[i + 1], previous[i + 1], currentWeight, previousWeight);
            current[i + 2] = Blend(current[i + 2], previous[i + 2], currentWeight, previousWeight);
            current[i + 3] = 255;
        }

        Buffer.BlockCopy(current, 0, previous, 0, current.Length);
    }

    private static byte Blend(byte current, byte previous, double currentWeight, double previousWeight)
    {
        return (byte)Math.Clamp((int)Math.Round(current * currentWeight + previous * previousWeight), 0, 255);
    }

    private static IBaseFilter? CreateSourceFilter(CameraDevice camera)
    {
        object? systemDeviceEnum = null;
        IEnumMoniker? enumMoniker = null;

        try
        {
            var enumType = Type.GetTypeFromCLSID(SystemDeviceEnumClsid, throwOnError: true)!;
            systemDeviceEnum = Activator.CreateInstance(enumType);
            if (systemDeviceEnum is not ICreateDevEnum createDevEnum)
            {
                return null;
            }

            var category = VideoInputDeviceCategory;
            if (createDevEnum.CreateClassEnumerator(ref category, out enumMoniker, 0) != 0 || enumMoniker is null)
            {
                return null;
            }

            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                try
                {
                    var name = ReadProperty(moniker, "FriendlyName");
                    var path = ReadProperty(moniker, "DevicePath") ?? GetDisplayName(moniker);
                    if (!CameraMatches(camera, name, path))
                    {
                        continue;
                    }

                    object? filter = null;
                    var baseFilterId = typeof(IBaseFilter).GUID;
                    moniker.BindToObject(null!, null, ref baseFilterId, out filter);
                    return filter as IBaseFilter;
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }

            return null;
        }
        finally
        {
            ReleaseComObject(enumMoniker);
            ReleaseComObject(systemDeviceEnum);
        }
    }

    private static bool CameraMatches(CameraDevice camera, string? name, string? path)
    {
        return (!string.IsNullOrWhiteSpace(camera.DevicePath)
                && string.Equals(path, camera.DevicePath, StringComparison.OrdinalIgnoreCase))
            || string.Equals(name, camera.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadProperty(IMoniker moniker, string propertyName)
    {
        object? propertyBagObject = null;

        try
        {
            var bagId = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(null!, null, ref bagId, out propertyBagObject);
            if (propertyBagObject is not IPropertyBag propertyBag)
            {
                return null;
            }

            propertyBag.Read(propertyName, out var value, IntPtr.Zero);
            return value as string;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(propertyBagObject);
        }
    }

    private static string? GetDisplayName(IMoniker moniker)
    {
        try
        {
            moniker.GetDisplayName(null!, null, out var displayName);
            return displayName;
        }
        catch
        {
            return null;
        }
    }

    private static void ThrowIfFailed(int result, string message)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{message}: 0x{result:X8}");
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private static void FreeMediaType(AMMediaType mediaType)
    {
        if (mediaType.FormatPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(mediaType.FormatPtr);
            mediaType.FormatPtr = IntPtr.Zero;
        }

        if (mediaType.UnknownPtr != IntPtr.Zero)
        {
            Marshal.Release(mediaType.UnknownPtr);
            mediaType.UnknownPtr = IntPtr.Zero;
        }
    }

    private sealed class CaptureCallback(DirectShowCameraPreviewService owner) : ISampleGrabberCB
    {
        public int SampleCB(double sampleTime, IntPtr sample)
        {
            return 0;
        }

        public int BufferCB(double sampleTime, IntPtr buffer, int bufferLength)
        {
            owner.OnBuffer(buffer, bufferLength);
            return 0;
        }
    }

    private sealed class DirectShowMediaType : IDisposable
    {
        public DirectShowMediaType(Guid majorType, Guid subtype, Guid formatType)
        {
            Value = new AMMediaType
            {
                MajorType = majorType,
                SubType = subtype,
                FormatType = formatType
            };
        }

        public AMMediaType Value { get; }

        public void Dispose()
        {
            FreeMediaType(Value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class AMMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        [MarshalAs(UnmanagedType.Bool)]
        public bool FixedSizeSamples;
        [MarshalAs(UnmanagedType.Bool)]
        public bool TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr UnknownPtr;
        public int FormatSize;
        public IntPtr FormatPtr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VideoInfoHeader
    {
        public DsRect Source;
        public DsRect Target;
        public int BitRate;
        public int BitErrorRate;
        public long AvgTimePerFrame;
        public BitmapInfoHeader Bitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DsRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid deviceClass, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        void Read(
            [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
            [MarshalAs(UnmanagedType.Struct)] out object? value,
            IntPtr errorLog);

        void Write(
            [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
            [MarshalAs(UnmanagedType.Struct)] ref object value);
    }

    [ComImport]
    [Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter
    {
    }

    [ComImport]
    [Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphBuilder
    {
        [PreserveSig]
        int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string name);
    }

    [ComImport]
    [Guid("93E5A4E0-2D50-11D2-ABFA-00A0C9C6E38D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureGraphBuilder2
    {
        [PreserveSig]
        int SetFiltergraph(IGraphBuilder graphBuilder);

        [PreserveSig]
        int GetFiltergraph(out IGraphBuilder graphBuilder);

        [PreserveSig]
        int SetOutputFileName(ref Guid type, [MarshalAs(UnmanagedType.LPWStr)] string fileName, out IBaseFilter filter, out IntPtr sink);

        [PreserveSig]
        int FindInterface(ref Guid category, ref Guid type, IBaseFilter filter, ref Guid riid, out IntPtr result);

        [PreserveSig]
        int RenderStream(ref Guid category, ref Guid type, IBaseFilter source, IBaseFilter compressor, IBaseFilter renderer);
    }

    [ComImport]
    [Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IMediaControl
    {
        [PreserveSig]
        int Run();

        [PreserveSig]
        int Pause();

        [PreserveSig]
        int Stop();
    }

    [ComImport]
    [Guid("6B652FFF-11FE-4FCE-92AD-0266B5D7C78F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISampleGrabber
    {
        [PreserveSig]
        int SetOneShot([MarshalAs(UnmanagedType.Bool)] bool oneShot);

        [PreserveSig]
        int SetMediaType([In] AMMediaType mediaType);

        [PreserveSig]
        int GetConnectedMediaType([Out] AMMediaType mediaType);

        [PreserveSig]
        int SetBufferSamples([MarshalAs(UnmanagedType.Bool)] bool bufferThem);

        [PreserveSig]
        int GetCurrentBuffer(ref int bufferSize, IntPtr buffer);

        [PreserveSig]
        int GetCurrentSample(out IntPtr sample);

        [PreserveSig]
        int SetCallback(ISampleGrabberCB? callback, int whichMethodToCallback);
    }

    [ComImport]
    [Guid("0579154A-2B53-4994-B0D0-E773148EFF85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISampleGrabberCB
    {
        [PreserveSig]
        int SampleCB(double sampleTime, IntPtr sample);

        [PreserveSig]
        int BufferCB(double sampleTime, IntPtr buffer, int bufferLength);
    }
}
