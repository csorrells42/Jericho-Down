namespace PodcastWorkbench.Video;

public sealed record TextureNativeRecordingResult(
    bool Success,
    string Path,
    int SamplesWritten,
    long BytesWritten,
    string DeviceMode,
    string MediaSubtype,
    int Width,
    int Height,
    double FramesPerSecond,
    string Status,
    string RecordingPipeline = "Media Foundation texture-native raw camera samples",
    bool RecordingDenoiseApplied = false,
    bool RecordingMatchesPreviewDenoise = false);

public sealed record TextureNativeRecordingOptions(
    bool ProcessedOutputEnabled,
    bool DenoiseEnabled,
    double DenoiseStrength);

public sealed record TextureNativeFrameInfo(
    int Width,
    int Height,
    double FramesPerSecond,
    string DeviceMode,
    string MediaSubtype,
    long FrameNumber);

public sealed record TextureNativePreviewFrame(
    int Width,
    int Height,
    double FramesPerSecond,
    string DeviceMode,
    string MediaSubtype,
    long FrameNumber,
    byte[]? Nv12PreviewBytes,
    int Nv12PreviewStride,
    byte[]? BgraPreviewBytes,
    int BgraPreviewStride)
{
    public static TextureNativePreviewFrame FromLease(TextureNativeFrameLease lease)
    {
        return new TextureNativePreviewFrame(
            lease.Width,
            lease.Height,
            lease.FramesPerSecond,
            lease.DeviceMode,
            lease.MediaSubtype,
            lease.FrameNumber,
            lease.Nv12PreviewBytes,
            lease.Nv12PreviewStride,
            lease.BgraPreviewBytes,
            lease.BgraPreviewStride);
    }
}

public sealed class TextureNativeFrameLease : IDisposable
{
    private IntPtr _resource;

    internal TextureNativeFrameLease(
        IntPtr resource,
        int subresource,
        int width,
        int height,
        double framesPerSecond,
        string deviceMode,
        string mediaSubtype,
        long frameNumber,
        byte[]? nv12PreviewBytes = null,
        int nv12PreviewStride = 0,
        byte[]? bgraPreviewBytes = null,
        int bgraPreviewStride = 0)
    {
        _resource = resource;
        Subresource = subresource;
        Width = width;
        Height = height;
        FramesPerSecond = framesPerSecond;
        DeviceMode = deviceMode;
        MediaSubtype = mediaSubtype;
        FrameNumber = frameNumber;
        Nv12PreviewBytes = nv12PreviewBytes;
        Nv12PreviewStride = nv12PreviewStride;
        BgraPreviewBytes = bgraPreviewBytes;
        BgraPreviewStride = bgraPreviewStride;
    }

    public IntPtr Resource => _resource;

    public int Subresource { get; }

    public int Width { get; }

    public int Height { get; }

    public double FramesPerSecond { get; }

    public string DeviceMode { get; }

    public string MediaSubtype { get; }

    public long FrameNumber { get; }

    public byte[]? Nv12PreviewBytes { get; }

    public int Nv12PreviewStride { get; }

    public byte[]? BgraPreviewBytes { get; }

    public int BgraPreviewStride { get; }

    public bool IsValid => _resource != IntPtr.Zero;

    public void Dispose()
    {
        var resource = Interlocked.Exchange(ref _resource, IntPtr.Zero);
        if (resource != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(resource);
        }
    }
}

public static class TextureNativeCameraRecorder
{
    public static TextureNativeCameraRecordingSession StartSession(
        string cameraName,
        CameraVideoMode? mode,
        string path)
    {
        return new TextureNativeCameraRecordingSession(cameraName, mode, path);
    }

    public static Task<TextureNativeRecordingResult> RecordAsync(
        string cameraName,
        CameraVideoMode? mode,
        string path,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => Record(cameraName, mode, path, duration, cancellationToken),
            cancellationToken);
    }

    private static TextureNativeRecordingResult Record(
        string cameraName,
        CameraVideoMode? mode,
        string path,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        using var _ = MediaFoundationCameraDeviceFactory.Startup();
        using var d3d11 = Direct3D11DeviceManager.Create();

        object? mediaSource = null;
        IMFSourceReader? reader = null;
        MediaFoundationTextureVideoRecorder? recorder = null;
        try
        {
            reader = MediaFoundationCameraDeviceFactory.CreateTextureSourceReader(
                cameraName,
                mode,
                d3d11.Manager,
                out mediaSource,
                enableAdvancedVideoProcessing: true,
                preferredSubtype: MediaFoundationGuids.MFVideoFormat_NV12,
                configureMediaType: true);

            var (width, height, fps, subtype) = ReadCurrentFormat(reader, mode);
            recorder = new MediaFoundationTextureVideoRecorder(
                path,
                width,
                height,
                fps,
                subtype,
                d3d11.Manager);

            var started = DateTimeOffset.UtcNow;
            var samplesWritten = 0;
            var nextSampleTime = 0L;
            var sampleDuration = Math.Max(1, (long)Math.Round(MediaFoundationInterop.TicksPerSecond / Math.Clamp(fps, 1d, 120d)));

            while (DateTimeOffset.UtcNow - started < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = reader.ReadSample(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out var actualStreamIndex,
                    out var streamFlags,
                    out var timestamp,
                    out var sampleObject);

                if (MediaFoundationInterop.Failed(result))
                {
                    return CreateResult(
                        false,
                        path,
                        samplesWritten,
                        d3d11.ModeName,
                        subtype,
                        width,
                        height,
                        fps,
                        $"Texture-native ReadSample failed: 0x{result:X8}");
                }

                if ((streamFlags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    break;
                }

                if (sampleObject is not IMFSample sample)
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                    continue;
                }

                try
                {
                    MediaFoundationInterop.ThrowIfFailed(sample.SetSampleTime(nextSampleTime));
                    MediaFoundationInterop.ThrowIfFailed(sample.SetSampleDuration(sampleDuration));
                    recorder.WriteSample(sample);
                    samplesWritten++;
                    nextSampleTime += sampleDuration;
                }
                finally
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                }
            }

            recorder.Stop();
            return CreateResult(
                samplesWritten > 0 && System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 4096,
                path,
                samplesWritten,
                d3d11.ModeName,
                subtype,
                width,
                height,
                fps,
                samplesWritten > 0
                    ? "Texture-native GPU sample recording completed."
                    : "No texture-native samples were written.");
        }
        catch (Exception ex)
        {
            return CreateResult(
                false,
                path,
                recorder?.SamplesWritten ?? 0,
                d3d11.ModeName,
                Guid.Empty,
                mode?.Width ?? 0,
                mode?.Height ?? 0,
                mode?.FramesPerSecond ?? 0d,
                ex.Message);
        }
        finally
        {
            recorder?.Dispose();
            MediaFoundationInterop.ReleaseComObject(reader);
            MediaFoundationInterop.ReleaseComObject(mediaSource);
        }
    }

    private static TextureNativeRecordingResult CreateResult(
        bool success,
        string path,
        int samplesWritten,
        string deviceMode,
        Guid subtype,
        int width,
        int height,
        double fps,
        string status)
    {
        var bytes = System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0;
        return new TextureNativeRecordingResult(
            success,
            path,
            samplesWritten,
            bytes,
            deviceMode,
            MediaFoundationInterop.FormatSubtype(subtype),
            width,
            height,
            fps,
            status);
    }

    private static (int Width, int Height, double Fps, Guid Subtype) ReadCurrentFormat(
        IMFSourceReader reader,
        CameraVideoMode? requestedMode)
    {
        var width = requestedMode?.Width ?? 1280;
        var height = requestedMode?.Height ?? 720;
        var fps = requestedMode?.FramesPerSecond ?? 30d;
        var subtype = MediaFoundationGuids.MFVideoFormat_NV12;

        var result = reader.GetCurrentMediaType(
            MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            out var currentType);
        if (MediaFoundationInterop.Failed(result))
        {
            return (width, height, fps, subtype);
        }

        try
        {
            if (MediaFoundationInterop.TryGetFrameSize(currentType, out var activeWidth, out var activeHeight))
            {
                width = activeWidth;
                height = activeHeight;
            }

            if (MediaFoundationInterop.TryGetFrameRate(currentType, out var activeFps))
            {
                fps = activeFps;
            }

            currentType.GetGUID(MediaFoundationGuids.MF_MT_SUBTYPE, out subtype);
            return (width, height, fps, subtype);
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(currentType);
        }
    }
}

public sealed class TextureNativeCameraStream : IDisposable
{
    private readonly object _stateLock = new();
    private readonly object _processedDenoiseLock = new();
    private readonly MediaFoundationCameraDeviceFactory.MediaFoundationScope _mediaFoundationScope;
    private readonly Direct3D11DeviceManager _deviceManager;
    private readonly object _mediaSource;
    private readonly IMFSourceReader _reader;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _captureTask;
    private readonly int _width;
    private readonly int _height;
    private readonly double _fps;
    private readonly Guid _subtype;
    private readonly long _sampleDuration;
    private MediaFoundationTextureVideoRecorder? _recorder;
    private MediaFoundationVideoRecorder? _processedRecorder;
    private byte[]? _previousProcessedDenoiseFrame;
    private string? _recordingPath;
    private string _recordingPipeline = "Media Foundation texture-native raw camera samples";
    private bool _recordingMatchesPreviewDenoise;
    private bool _processedRecordingDenoiseEnabled;
    private double _processedRecordingDenoiseStrength = 2d;
    private bool _isPaused;
    private bool _isStopping;
    private long _nextSampleTime;
    private long _framesRead;
    private string _status = "Texture-native camera stream started.";

    public TextureNativeCameraStream(string cameraName, CameraVideoMode? mode)
    {
        _mediaFoundationScope = MediaFoundationCameraDeviceFactory.Startup();
        _deviceManager = Direct3D11DeviceManager.Create();
        _reader = MediaFoundationCameraDeviceFactory.CreateTextureSourceReader(
            cameraName,
            mode,
            _deviceManager.Manager,
            out _mediaSource,
            enableAdvancedVideoProcessing: true,
            preferredSubtype: MediaFoundationGuids.MFVideoFormat_NV12,
            configureMediaType: true);
        (_width, _height, _fps, _subtype) = ReadCurrentFormat(_reader, mode);
        _sampleDuration = Math.Max(1, (long)Math.Round(MediaFoundationInterop.TicksPerSecond / Math.Clamp(_fps, 1d, 120d)));
        _captureTask = Task.Run(() => CaptureLoop(_cancellation.Token));
    }

    public event EventHandler<TextureNativeFrameInfo>? FrameAvailable;
    public event EventHandler<TextureNativeFrameLease>? TextureFrameAvailable;
    public event EventHandler<string>? StatusChanged;

    public int Width => _width;

    public int Height => _height;

    public double FramesPerSecond => _fps;

    public string DeviceMode => _deviceManager.ModeName;

    public string MediaSubtype => MediaFoundationInterop.FormatSubtype(_subtype);

    public long FramesRead => Interlocked.Read(ref _framesRead);

    public bool IsRecording
    {
        get
        {
            lock (_stateLock)
            {
                return _recorder is not null || _processedRecorder is not null;
            }
        }
    }

    public bool StartRecording(string path, TextureNativeRecordingOptions? options = null)
    {
        lock (_stateLock)
        {
            if (_recorder is not null || _processedRecorder is not null)
            {
                return true;
            }

            if (options?.ProcessedOutputEnabled == true)
            {
                _processedRecorder = new MediaFoundationVideoRecorder(
                    path,
                    _width,
                    _height,
                    _fps,
                    d3dManager: null);
                _processedRecordingDenoiseEnabled = options.DenoiseEnabled;
                _processedRecordingDenoiseStrength = options.DenoiseStrength;
                ResetProcessedDenoiseHistory();
                _recordingPipeline = "Texture-native processed BGRA bridge";
                _recordingMatchesPreviewDenoise = true;
            }
            else
            {
                _recorder = new MediaFoundationTextureVideoRecorder(
                    path,
                    _width,
                    _height,
                    _fps,
                    _subtype,
                    _deviceManager.Manager);
                _processedRecordingDenoiseEnabled = false;
                ResetProcessedDenoiseHistory();
                _recordingPipeline = "Media Foundation texture-native raw camera samples";
                _recordingMatchesPreviewDenoise = options?.DenoiseEnabled != true;
            }

            _recordingPath = path;
            _nextSampleTime = 0;
            _isPaused = false;
            _status = $"Texture-native GPU recording started: {System.IO.Path.GetFileName(path)}";
            StatusChanged?.Invoke(this, _status);
            return true;
        }
    }

    public void PauseRecording()
    {
        lock (_stateLock)
        {
            _isPaused = true;
            _processedRecorder?.Pause();
            _status = "Texture-native GPU recording paused.";
            StatusChanged?.Invoke(this, _status);
        }
    }

    public void ResumeRecording()
    {
        lock (_stateLock)
        {
            _isPaused = false;
            _processedRecorder?.Resume();
            _status = "Texture-native GPU recording resumed.";
            StatusChanged?.Invoke(this, _status);
        }
    }

    public TextureNativeRecordingResult? StopRecording()
    {
        MediaFoundationTextureVideoRecorder? recorder;
        MediaFoundationVideoRecorder? processedRecorder;
        string? path;
        string status;
        string recordingPipeline;
        bool processedDenoiseEnabled;
        bool recordingMatchesPreviewDenoise;
        lock (_stateLock)
        {
            recorder = _recorder;
            processedRecorder = _processedRecorder;
            path = _recordingPath;
            status = _status;
            recordingPipeline = _recordingPipeline;
            processedDenoiseEnabled = _processedRecordingDenoiseEnabled;
            recordingMatchesPreviewDenoise = _recordingMatchesPreviewDenoise;
            _recorder = null;
            _processedRecorder = null;
            _recordingPath = null;
            _isPaused = false;
            ResetProcessedDenoiseHistory();
            _processedRecordingDenoiseEnabled = false;
            _recordingMatchesPreviewDenoise = false;
            _recordingPipeline = "Media Foundation texture-native raw camera samples";
        }

        if ((recorder is null && processedRecorder is null) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            recorder?.Stop();
            processedRecorder?.Stop();
            var bytes = System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0;
            var samplesWritten = recorder?.SamplesWritten ?? processedRecorder?.SamplesWritten ?? 0;
            var success = samplesWritten > 0 && bytes > 4096;
            status = success
                ? processedRecorder is not null
                    ? "Texture-native processed bridge recording completed."
                    : "Texture-native shared stream recording completed."
                : status;
            return new TextureNativeRecordingResult(
                success,
                path,
                samplesWritten,
                bytes,
                _deviceManager.ModeName,
                processedRecorder is not null ? "rgb32" : MediaFoundationInterop.FormatSubtype(_subtype),
                _width,
                _height,
                _fps,
                status,
                recordingPipeline,
                processedDenoiseEnabled,
                recordingMatchesPreviewDenoise);
        }
        finally
        {
            recorder?.Dispose();
            processedRecorder?.Dispose();
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (_isStopping)
            {
                return;
            }

            _isStopping = true;
        }

        _cancellation.Cancel();
        try
        {
            _captureTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        StopRecording();
    }

    public void Dispose()
    {
        Stop();
        _cancellation.Dispose();
        MediaFoundationInterop.ReleaseComObject(_reader);
        MediaFoundationInterop.ReleaseComObject(_mediaSource);
        _deviceManager.Dispose();
        _mediaFoundationScope.Dispose();
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = _reader.ReadSample(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out _,
                    out var streamFlags,
                    out _,
                    out var sampleObject);

                if (MediaFoundationInterop.Failed(result))
                {
                    ReportStatus($"Texture-native shared stream read failed: 0x{result:X8}");
                    break;
                }

                if ((streamFlags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    ReportStatus("Texture-native shared stream ended.");
                    break;
                }

                if (sampleObject is not IMFSample sample)
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                    continue;
                }

                try
                {
                    var frameNumber = Interlocked.Increment(ref _framesRead);
                    using var frameLease = TryCreateFrameLease(sample, frameNumber);
                    FrameAvailable?.Invoke(
                        this,
                        new TextureNativeFrameInfo(
                            _width,
                            _height,
                            _fps,
                            _deviceManager.ModeName,
                            MediaFoundationInterop.FormatSubtype(_subtype),
                            frameNumber));
                    if (frameLease is not null)
                    {
                        TextureFrameAvailable?.Invoke(this, frameLease);
                    }

                    WriteRecordingSample(sample);
                }
                finally
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                }
            }
        }
        catch (Exception ex)
        {
            ReportStatus(ex.Message);
        }
    }

    private void WriteRecordingSample(IMFSample sample)
    {
        MediaFoundationTextureVideoRecorder? recorder;
        MediaFoundationVideoRecorder? processedRecorder;
        bool isPaused;
        bool processedDenoiseEnabled;
        double processedDenoiseStrength;
        lock (_stateLock)
        {
            recorder = _recorder;
            processedRecorder = _processedRecorder;
            isPaused = _isPaused;
            processedDenoiseEnabled = _processedRecordingDenoiseEnabled;
            processedDenoiseStrength = _processedRecordingDenoiseStrength;
        }

        if (isPaused || (recorder is null && processedRecorder is null))
        {
            return;
        }

        if (processedRecorder is not null)
        {
            if (!TryCreateBgraFrame(sample, out var bgraBytes))
            {
                return;
            }

            if (processedDenoiseEnabled)
            {
                lock (_processedDenoiseLock)
                {
                    ApplyTemporalDenoise(bgraBytes, processedDenoiseStrength, ref _previousProcessedDenoiseFrame);
                }
            }
            else
            {
                ResetProcessedDenoiseHistory();
            }

            processedRecorder.WriteFrame(bgraBytes);
        }
        else if (recorder is not null)
        {
            MediaFoundationInterop.ThrowIfFailed(sample.SetSampleTime(_nextSampleTime));
            MediaFoundationInterop.ThrowIfFailed(sample.SetSampleDuration(_sampleDuration));
            recorder.WriteSample(sample);
        }

        _nextSampleTime += _sampleDuration;
    }

    private void ReportStatus(string status)
    {
        lock (_stateLock)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private void ResetProcessedDenoiseHistory()
    {
        lock (_processedDenoiseLock)
        {
            _previousProcessedDenoiseFrame = null;
        }
    }

    private bool TryCreateBgraFrame(IMFSample sample, out byte[] bgraBytes)
    {
        bgraBytes = [];
        IMFMediaBuffer? buffer = null;
        try
        {
            var bufferResult = sample.GetBufferByIndex(0, out buffer);
            if (MediaFoundationInterop.Failed(bufferResult) || buffer is null)
            {
                return false;
            }

            if (_subtype != MediaFoundationGuids.MFVideoFormat_NV12)
            {
                return false;
            }

            var result = buffer.Lock(out var source, out _, out var currentLength);
            if (MediaFoundationInterop.Failed(result) || source == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var converted = ConvertNv12ToBgra(source, currentLength, _width, _height, out _);
                if (converted is null)
                {
                    return false;
                }

                bgraBytes = converted;
                return true;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(buffer);
        }
    }

    private static void ApplyTemporalDenoise(byte[] current, double denoiseStrength, ref byte[]? previousFrame)
    {
        var previous = previousFrame;
        if (previous is null || previous.Length != current.Length)
        {
            previousFrame = (byte[])current.Clone();
            return;
        }

        var strength = Math.Clamp(denoiseStrength, 0.5d, 8d);
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

    private TextureNativeFrameLease? TryCreateFrameLease(IMFSample sample, long frameNumber)
    {
        IMFMediaBuffer? buffer = null;
        try
        {
            var bufferResult = sample.GetBufferByIndex(0, out buffer);
            if (MediaFoundationInterop.Failed(bufferResult) || buffer is null)
            {
                return null;
            }

            var dxgiBuffer = QueryDxgiBuffer(buffer);
            if (dxgiBuffer is null)
            {
                return null;
            }

            try
            {
                var subresource = 0;
                dxgiBuffer.GetSubresourceIndex(out subresource);
                var resourceResult = dxgiBuffer.GetResource(
                    Direct3D11DeviceManager.ID3D11Texture2D,
                    out var resource);
                if (MediaFoundationInterop.Failed(resourceResult) || resource == IntPtr.Zero)
                {
                    return null;
                }

                var nv12PreviewBytes = TryCreateNv12Preview(buffer, out var nv12PreviewStride);
                var bgraPreviewStride = 0;
                var bgraPreviewBytes = frameNumber % 16 == 0
                    ? TryCreateBgraPreview(buffer, frameNumber, out bgraPreviewStride)
                    : null;

                return new TextureNativeFrameLease(
                    resource,
                    subresource,
                    _width,
                    _height,
                    _fps,
                    _deviceManager.ModeName,
                    MediaFoundationInterop.FormatSubtype(_subtype),
                    frameNumber,
                    nv12PreviewBytes,
                    nv12PreviewStride,
                    bgraPreviewBytes,
                    bgraPreviewStride);
            }
            finally
            {
                MediaFoundationInterop.ReleaseComObject(dxgiBuffer);
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(buffer);
        }
    }

    private byte[]? TryCreateNv12Preview(IMFMediaBuffer buffer, out int nv12Stride)
    {
        nv12Stride = 0;
        if (_subtype != MediaFoundationGuids.MFVideoFormat_NV12)
        {
            return null;
        }

        var result = buffer.Lock(out var source, out _, out var currentLength);
        if (MediaFoundationInterop.Failed(result) || source == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var pitch = Math.Max(_width, currentLength * 2 / Math.Max(1, _height * 3));
            var requiredLength = pitch * _height + pitch * ((_height + 1) / 2);
            if (currentLength < requiredLength)
            {
                return null;
            }

            var nv12 = new byte[requiredLength];
            System.Runtime.InteropServices.Marshal.Copy(source, nv12, 0, requiredLength);
            nv12Stride = pitch;
            return nv12;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    private byte[]? TryCreateBgraPreview(IMFMediaBuffer buffer, long frameNumber, out int bgraStride)
    {
        bgraStride = 0;
        if (frameNumber % 4 != 0 || _subtype != MediaFoundationGuids.MFVideoFormat_NV12)
        {
            return null;
        }

        var result = buffer.Lock(out var source, out _, out var currentLength);
        if (MediaFoundationInterop.Failed(result) || source == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ConvertNv12ToBgra(source, currentLength, _width, _height, out bgraStride);
        }
        finally
        {
            buffer.Unlock();
        }
    }

    private static byte[]? ConvertNv12ToBgra(IntPtr source, int sourceLength, int width, int height, out int bgraStride)
    {
        bgraStride = width * 4;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var pitch = Math.Max(width, sourceLength * 2 / Math.Max(1, height * 3));
        var requiredLength = pitch * height + pitch * ((height + 1) / 2);
        if (sourceLength < requiredLength)
        {
            return null;
        }

        var nv12 = new byte[sourceLength];
        System.Runtime.InteropServices.Marshal.Copy(source, nv12, 0, sourceLength);
        var bgra = new byte[bgraStride * height];
        var uvOffset = pitch * height;
        for (var y = 0; y < height; y++)
        {
            var yRow = y * pitch;
            var uvRow = uvOffset + (y / 2) * pitch;
            var outputRow = y * bgraStride;
            for (var x = 0; x < width; x++)
            {
                var yy = nv12[yRow + x];
                var uvIndex = uvRow + (x & ~1);
                var u = nv12[uvIndex];
                var v = nv12[uvIndex + 1];
                var c = yy - 16;
                var d = u - 128;
                var e = v - 128;
                var r = ClampToByte((298 * c + 409 * e + 128) >> 8);
                var g = ClampToByte((298 * c - 100 * d - 208 * e + 128) >> 8);
                var b = ClampToByte((298 * c + 516 * d + 128) >> 8);
                var output = outputRow + x * 4;
                bgra[output] = b;
                bgra[output + 1] = g;
                bgra[output + 2] = r;
                bgra[output + 3] = 255;
            }
        }

        return bgra;
    }

    private static byte ClampToByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }

    private static IMFDXGIBuffer? QueryDxgiBuffer(IMFMediaBuffer buffer)
    {
        var unknown = IntPtr.Zero;
        var dxgiBufferPointer = IntPtr.Zero;
        try
        {
            unknown = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(buffer);
            var dxgiBufferId = typeof(IMFDXGIBuffer).GUID;
            if (System.Runtime.InteropServices.Marshal.QueryInterface(unknown, in dxgiBufferId, out dxgiBufferPointer) < 0
                || dxgiBufferPointer == IntPtr.Zero)
            {
                return null;
            }

            return (IMFDXGIBuffer)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(dxgiBufferPointer);
        }
        finally
        {
            if (dxgiBufferPointer != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.Release(dxgiBufferPointer);
            }

            if (unknown != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.Release(unknown);
            }
        }
    }

    private static (int Width, int Height, double Fps, Guid Subtype) ReadCurrentFormat(
        IMFSourceReader reader,
        CameraVideoMode? requestedMode)
    {
        var width = requestedMode?.Width ?? 1280;
        var height = requestedMode?.Height ?? 720;
        var fps = requestedMode?.FramesPerSecond ?? 30d;
        var subtype = MediaFoundationGuids.MFVideoFormat_NV12;

        var result = reader.GetCurrentMediaType(
            MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            out var currentType);
        if (MediaFoundationInterop.Failed(result))
        {
            return (width, height, fps, subtype);
        }

        try
        {
            if (MediaFoundationInterop.TryGetFrameSize(currentType, out var activeWidth, out var activeHeight))
            {
                width = activeWidth;
                height = activeHeight;
            }

            if (MediaFoundationInterop.TryGetFrameRate(currentType, out var activeFps))
            {
                fps = activeFps;
            }

            currentType.GetGUID(MediaFoundationGuids.MF_MT_SUBTYPE, out subtype);
            return (width, height, fps, subtype);
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(currentType);
        }
    }
}

public sealed class TextureNativeCameraRecordingSession : IDisposable
{
    private readonly object _stateLock = new();
    private readonly MediaFoundationCameraDeviceFactory.MediaFoundationScope _mediaFoundationScope;
    private readonly Direct3D11DeviceManager _deviceManager;
    private readonly object _mediaSource;
    private readonly IMFSourceReader _reader;
    private readonly MediaFoundationTextureVideoRecorder _recorder;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _recordingTask;
    private readonly int _width;
    private readonly int _height;
    private readonly double _fps;
    private readonly Guid _subtype;
    private readonly long _sampleDuration;
    private bool _isPaused;
    private bool _isStopping;
    private long _nextSampleTime;
    private string _status = "Texture-native GPU recording started.";

    internal TextureNativeCameraRecordingSession(string cameraName, CameraVideoMode? mode, string path)
    {
        Path = path;
        _mediaFoundationScope = MediaFoundationCameraDeviceFactory.Startup();
        _deviceManager = Direct3D11DeviceManager.Create();
        _reader = MediaFoundationCameraDeviceFactory.CreateTextureSourceReader(
            cameraName,
            mode,
            _deviceManager.Manager,
            out _mediaSource,
            enableAdvancedVideoProcessing: true,
            preferredSubtype: MediaFoundationGuids.MFVideoFormat_NV12,
            configureMediaType: true);
        (_width, _height, _fps, _subtype) = ReadCurrentFormat(_reader, mode);
        _sampleDuration = Math.Max(1, (long)Math.Round(MediaFoundationInterop.TicksPerSecond / Math.Clamp(_fps, 1d, 120d)));
        _recorder = new MediaFoundationTextureVideoRecorder(
            path,
            _width,
            _height,
            _fps,
            _subtype,
            _deviceManager.Manager);
        _recordingTask = Task.Run(() => CaptureLoop(_cancellation.Token));
    }

    public string Path { get; }

    public bool IsPaused
    {
        get
        {
            lock (_stateLock)
            {
                return _isPaused;
            }
        }
    }

    public int SamplesWritten => _recorder.SamplesWritten;

    public void Pause()
    {
        lock (_stateLock)
        {
            _isPaused = true;
            _status = "Texture-native GPU recording paused.";
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            _isPaused = false;
            _status = "Texture-native GPU recording resumed.";
        }
    }

    public TextureNativeRecordingResult Stop()
    {
        lock (_stateLock)
        {
            if (_isStopping)
            {
                return CreateResult(_recorder.SamplesWritten > 0, _status);
            }

            _isStopping = true;
        }

        _cancellation.Cancel();
        try
        {
            _recordingTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        _recorder.Stop();
        return CreateResult(
            _recorder.SamplesWritten > 0 && System.IO.File.Exists(Path) && new System.IO.FileInfo(Path).Length > 4096,
            _recorder.SamplesWritten > 0
                ? "Texture-native GPU sample recording completed."
                : _status);
    }

    public void Dispose()
    {
        Stop();
        _cancellation.Dispose();
        _recorder.Dispose();
        MediaFoundationInterop.ReleaseComObject(_reader);
        MediaFoundationInterop.ReleaseComObject(_mediaSource);
        _deviceManager.Dispose();
        _mediaFoundationScope.Dispose();
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (IsPaused)
                {
                    Thread.Sleep(20);
                    continue;
                }

                var result = _reader.ReadSample(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out _,
                    out var streamFlags,
                    out _,
                    out var sampleObject);

                if (MediaFoundationInterop.Failed(result))
                {
                    lock (_stateLock)
                    {
                        _status = $"Texture-native ReadSample failed: 0x{result:X8}";
                    }
                    break;
                }

                if ((streamFlags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    lock (_stateLock)
                    {
                        _status = "Texture-native camera stream ended.";
                    }
                    break;
                }

                if (sampleObject is not IMFSample sample)
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                    continue;
                }

                try
                {
                    MediaFoundationInterop.ThrowIfFailed(sample.SetSampleTime(_nextSampleTime));
                    MediaFoundationInterop.ThrowIfFailed(sample.SetSampleDuration(_sampleDuration));
                    _recorder.WriteSample(sample);
                    _nextSampleTime += _sampleDuration;
                }
                finally
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _status = ex.Message;
            }
        }
    }

    private TextureNativeRecordingResult CreateResult(bool success, string status)
    {
        var bytes = System.IO.File.Exists(Path) ? new System.IO.FileInfo(Path).Length : 0;
        return new TextureNativeRecordingResult(
            success,
            Path,
            _recorder.SamplesWritten,
            bytes,
            _deviceManager.ModeName,
            MediaFoundationInterop.FormatSubtype(_subtype),
            _width,
            _height,
            _fps,
            status);
    }

    private static (int Width, int Height, double Fps, Guid Subtype) ReadCurrentFormat(
        IMFSourceReader reader,
        CameraVideoMode? requestedMode)
    {
        var width = requestedMode?.Width ?? 1280;
        var height = requestedMode?.Height ?? 720;
        var fps = requestedMode?.FramesPerSecond ?? 30d;
        var subtype = MediaFoundationGuids.MFVideoFormat_NV12;

        var result = reader.GetCurrentMediaType(
            MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            out var currentType);
        if (MediaFoundationInterop.Failed(result))
        {
            return (width, height, fps, subtype);
        }

        try
        {
            if (MediaFoundationInterop.TryGetFrameSize(currentType, out var activeWidth, out var activeHeight))
            {
                width = activeWidth;
                height = activeHeight;
            }

            if (MediaFoundationInterop.TryGetFrameRate(currentType, out var activeFps))
            {
                fps = activeFps;
            }

            currentType.GetGUID(MediaFoundationGuids.MF_MT_SUBTYPE, out subtype);
            return (width, height, fps, subtype);
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(currentType);
        }
    }
}

internal sealed class MediaFoundationTextureVideoRecorder : IDisposable
{
    private readonly object _lock = new();
    private readonly IMFDXGIDeviceManager _deviceManager;
    private IMFSinkWriter? _writer;
    private int _streamIndex;
    private bool _isFinalized;

    public MediaFoundationTextureVideoRecorder(
        string path,
        int width,
        int height,
        double framesPerSecond,
        Guid inputSubtype,
        IMFDXGIDeviceManager deviceManager)
    {
        Path = path;
        Width = width;
        Height = height;
        FramesPerSecond = Math.Clamp(framesPerSecond, 1d, 120d);
        InputSubtype = inputSubtype;
        _deviceManager = deviceManager;

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        InitializeWriter();
    }

    public string Path { get; }

    public int Width { get; }

    public int Height { get; }

    public double FramesPerSecond { get; }

    public Guid InputSubtype { get; }

    public int SamplesWritten { get; private set; }

    public void WriteSample(IMFSample sample)
    {
        lock (_lock)
        {
            if (_writer is null || _isFinalized)
            {
                return;
            }

            MediaFoundationInterop.ThrowIfFailed(_writer.WriteSample(_streamIndex, sample));
            SamplesWritten++;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_writer is null || _isFinalized)
            {
                return;
            }

            try
            {
                _isFinalized = true;
                var result = _writer.Finalize_();
                if (MediaFoundationInterop.Failed(result) && SamplesWritten > 0)
                {
                    MediaFoundationInterop.ThrowIfFailed(result);
                }
            }
            finally
            {
                MediaFoundationInterop.ReleaseComObject(_writer);
                _writer = null;
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void InitializeWriter()
    {
        MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateAttributes(out var writerAttributes, 2));
        try
        {
            MediaFoundationInterop.ThrowIfFailed(writerAttributes.SetUINT32(
                MediaFoundationGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
                1));
            MediaFoundationInterop.ThrowIfFailed(writerAttributes.SetUnknown(
                MediaFoundationGuids.MF_SOURCE_READER_D3D_MANAGER,
                _deviceManager));
            MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateSinkWriterFromURL(
                Path,
                IntPtr.Zero,
                writerAttributes,
                out _writer));
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(writerAttributes);
        }

        IMFMediaType? outputType = null;
        IMFMediaType? inputType = null;
        try
        {
            MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateMediaType(out outputType));
            ConfigureVideoType(outputType, MediaFoundationGuids.MFVideoFormat_H264, EstimateBitrate(), includePixelAspect: true);
            MediaFoundationInterop.ThrowIfFailed(_writer.AddStream(outputType, out _streamIndex));

            MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateMediaType(out inputType));
            ConfigureVideoType(inputType, InputSubtype, bitrate: null, includePixelAspect: true);
            MediaFoundationInterop.ThrowIfFailed(_writer.SetInputMediaType(_streamIndex, inputType, null));
            MediaFoundationInterop.ThrowIfFailed(_writer.BeginWriting());
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(outputType);
            MediaFoundationInterop.ReleaseComObject(inputType);
        }
    }

    private void ConfigureVideoType(IMFMediaType mediaType, Guid subtype, int? bitrate, bool includePixelAspect)
    {
        var (fpsNumerator, fpsDenominator) = CreateFrameRateRatio(FramesPerSecond);
        MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
            MediaFoundationGuids.MF_MT_MAJOR_TYPE,
            MediaFoundationGuids.MFMediaType_Video));
        MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
            MediaFoundationGuids.MF_MT_SUBTYPE,
            subtype));
        MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT64(
            MediaFoundationGuids.MF_MT_FRAME_SIZE,
            MediaFoundationInterop.PackRatio(Width, Height)));
        MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT64(
            MediaFoundationGuids.MF_MT_FRAME_RATE,
            MediaFoundationInterop.PackRatio(fpsNumerator, fpsDenominator)));
        MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT32(
            MediaFoundationGuids.MF_MT_INTERLACE_MODE,
            MediaFoundationInterop.MFVideoInterlace_Progressive));

        if (includePixelAspect)
        {
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT64(
                MediaFoundationGuids.MF_MT_PIXEL_ASPECT_RATIO,
                MediaFoundationInterop.PackRatio(1, 1)));
        }

        if (bitrate is int value)
        {
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT32(
                MediaFoundationGuids.MF_MT_AVG_BITRATE,
                value));
        }
    }

    private int EstimateBitrate()
    {
        var megapixels = Width * Height / 1_000_000d;
        var frameRateFactor = FramesPerSecond / 30d;
        return (int)Math.Round(Math.Clamp(megapixels * frameRateFactor * 5_500_000d, 8_000_000d, 64_000_000d));
    }

    private static (int Numerator, int Denominator) CreateFrameRateRatio(double fps)
    {
        if (Math.Abs(fps - 29.97d) < 0.02d)
        {
            return (30000, 1001);
        }

        if (Math.Abs(fps - 59.94d) < 0.02d)
        {
            return (60000, 1001);
        }

        return ((int)Math.Round(Math.Clamp(fps, 1d, 240d)), 1);
    }
}
