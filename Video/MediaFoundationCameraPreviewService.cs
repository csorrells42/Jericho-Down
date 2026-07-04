using System.Runtime.InteropServices;

namespace PodcastWorkbench.Video;

public sealed class MediaFoundationCameraPreviewService : IDisposable
{
    private readonly object _recordingLock = new();
    private MediaFoundationCameraDeviceFactory.MediaFoundationScope? _mediaFoundationScope;
    private IMFSourceReader? _reader;
    private object? _mediaSource;
    private Direct3D12DeviceManager? _direct3D12;
    private CancellationTokenSource? _cancellation;
    private Task? _captureTask;
    private byte[]? _previousDenoiseFrame;
    private MediaFoundationVideoRecorder? _recorder;
    private string? _recordingPath;
    private int _activeWidth = 1280;
    private int _activeHeight = 720;
    private double _activeFramesPerSecond = 30d;

    public event EventHandler<CameraFrame>? FrameAvailable;
    public event EventHandler<string>? StatusChanged;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool DenoiseEnabled { get; set; }

    public double DenoiseStrength { get; set; } = 2d;

    public bool IsRecording
    {
        get
        {
            lock (_recordingLock)
            {
                return _recorder is not null;
            }
        }
    }

    public bool Start(CameraDevice camera, CameraVideoMode? mode)
    {
        Stop();

        if (!OperatingSystem.IsWindows())
        {
            StatusChanged?.Invoke(this, "Media Foundation camera capture requires Windows");
            return false;
        }

        try
        {
            _cancellation = new CancellationTokenSource();
            var startup = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureTask = Task.Run(() => CaptureLoop(
                camera,
                mode,
                _cancellation.Token,
                startup));
            if (!startup.Task.Wait(TimeSpan.FromSeconds(5)))
            {
                Stop();
                StatusChanged?.Invoke(this, $"Could not start Media Foundation preview: timed out opening {camera.Name}");
                return false;
            }

            var startupError = startup.Task.Result;
            if (!string.IsNullOrWhiteSpace(startupError))
            {
                Stop();
                StatusChanged?.Invoke(this, $"Could not start Media Foundation preview: {startupError}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            CleanupPreviewObjects();
            StatusChanged?.Invoke(this, $"Could not start Media Foundation preview: {ex.Message}");
            return false;
        }
    }

    public bool StartRecording(string path, CameraVideoMode? mode)
    {
        lock (_recordingLock)
        {
            if (_recorder is not null)
            {
                return true;
            }

            var width = _activeWidth > 0 ? _activeWidth : mode?.Width ?? 1280;
            var height = _activeHeight > 0 ? _activeHeight : mode?.Height ?? 720;
            var fps = _activeFramesPerSecond > 0 ? _activeFramesPerSecond : mode?.FramesPerSecond ?? 30d;
            try
            {
                _recorder = new MediaFoundationVideoRecorder(path, width, height, fps, d3dManager: null);
                _recordingPath = path;
                StatusChanged?.Invoke(this, $"Recording video with Media Foundation: {System.IO.Path.GetFileName(path)}");
                return true;
            }
            catch (Exception ex)
            {
                _recorder?.Dispose();
                _recorder = null;
                _recordingPath = null;
                StatusChanged?.Invoke(this, $"Video recording failed: {ex.Message}");
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
        lock (_recordingLock)
        {
            var recorder = _recorder;
            var path = _recordingPath;
            _recorder = null;
            _recordingPath = null;
            recorder?.Stop();
            recorder?.Dispose();
            return path;
        }
    }

    public void Stop()
    {
        var captureTask = _captureTask;
        _cancellation?.Cancel();

        try
        {
            captureTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        _captureTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
        StopRecording();
        if (captureTask is null || captureTask.IsCompleted)
        {
            CleanupPreviewObjects();
        }
        else
        {
            ResetPreviewState();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void CaptureLoop(
        CameraDevice camera,
        CameraVideoMode? mode,
        CancellationToken cancellationToken,
        TaskCompletionSource<string?> startup)
    {
        IMFSourceReader? reader = null;
        object? mediaSource = null;
        MediaFoundationCameraDeviceFactory.MediaFoundationScope? mediaFoundationScope = null;
        try
        {
            mediaFoundationScope = MediaFoundationCameraDeviceFactory.Startup();
            reader = CreateSourceReaderWithAccelerationFallback(
                camera,
                mode,
                out mediaSource);
            _mediaFoundationScope = mediaFoundationScope;
            _reader = reader;
            _mediaSource = mediaSource;
            UpdateActiveFormat(reader, mode);
            var width = _activeWidth;
            var height = _activeHeight;
            StatusChanged?.Invoke(this, $"Starting Media Foundation preview: {camera.Name}");
            startup.TrySetResult(null);

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = reader.ReadSample(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out _,
                    out var streamFlags,
                    out _,
                    out var sampleObject);

                if (MediaFoundationInterop.Failed(result))
                {
                    StatusChanged?.Invoke(this, $"Camera read failed: 0x{result:X8}");
                    Thread.Sleep(50);
                    continue;
                }

                if ((streamFlags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    StatusChanged?.Invoke(this, "Camera preview ended");
                    break;
                }

                if (sampleObject is not IMFSample sample)
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                    continue;
                }

                try
                {
                    if (TryReadFrame(sample, width, height, out var frame))
                    {
                        if (DenoiseEnabled)
                        {
                            ApplyTemporalDenoise(frame.BgraBytes);
                        }
                        else
                        {
                            _previousDenoiseFrame = null;
                        }

                        FrameAvailable?.Invoke(this, frame);

                        lock (_recordingLock)
                        {
                            try
                            {
                                _recorder?.WriteFrame(frame.BgraBytes);
                            }
                            catch (Exception ex)
                            {
                                StatusChanged?.Invoke(this, $"Video frame write failed: {ex.Message}");
                                _recorder?.Dispose();
                                _recorder = null;
                                _recordingPath = null;
                            }
                        }
                    }
                }
                finally
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            startup.TrySetResult(ex.Message);
            StatusChanged?.Invoke(this, ex.Message);
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(reader);
            MediaFoundationInterop.ReleaseComObject(mediaSource);
            _direct3D12?.Dispose();
            mediaFoundationScope?.Dispose();
            if (ReferenceEquals(_reader, reader))
            {
                _reader = null;
            }

            if (ReferenceEquals(_mediaSource, mediaSource))
            {
                _mediaSource = null;
            }

            if (ReferenceEquals(_mediaFoundationScope, mediaFoundationScope))
            {
                _mediaFoundationScope = null;
            }

            _direct3D12 = null;
            startup.TrySetResult("Capture loop ended before startup completed.");
        }
    }

    private static bool TryReadFrame(IMFSample sample, int width, int height, out CameraFrame frame)
    {
        frame = new CameraFrame([], 0, 0, 0);
        IMFMediaBuffer? buffer = null;
        try
        {
            var result = sample.GetBufferByIndex(0, out buffer);
            if (MediaFoundationInterop.Failed(result))
            {
                MediaFoundationInterop.ThrowIfFailed(sample.ConvertToContiguousBuffer(out buffer));
            }

            MediaFoundationInterop.ThrowIfFailed(buffer.Lock(out var source, out _, out var currentLength));
            try
            {
                var stride = width * 4;
                var expectedBytes = stride * height;
                if (currentLength < expectedBytes)
                {
                    return false;
                }

                var bytes = new byte[expectedBytes];
                Marshal.Copy(source, bytes, 0, expectedBytes);
                frame = new CameraFrame(bytes, width, height, stride);
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

    private void CleanupPreviewObjects()
    {
        ResetPreviewState();
        MediaFoundationInterop.ReleaseComObject(_reader);
        MediaFoundationInterop.ReleaseComObject(_mediaSource);
        _reader = null;
        _mediaSource = null;
        _direct3D12?.Dispose();
        _direct3D12 = null;
        _mediaFoundationScope?.Dispose();
        _mediaFoundationScope = null;
    }

    private void ResetPreviewState()
    {
        _previousDenoiseFrame = null;
        _activeWidth = 1280;
        _activeHeight = 720;
        _activeFramesPerSecond = 30d;
    }

    private void UpdateActiveFormat(IMFSourceReader reader, CameraVideoMode? requestedMode)
    {
        var result = reader.GetCurrentMediaType(
            MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            out var currentType);
        if (MediaFoundationInterop.Failed(result))
        {
            _activeWidth = requestedMode?.Width ?? 1280;
            _activeHeight = requestedMode?.Height ?? 720;
            _activeFramesPerSecond = requestedMode?.FramesPerSecond ?? 30d;
            return;
        }

        try
        {
            if (MediaFoundationInterop.TryGetFrameSize(currentType, out var width, out var height))
            {
                _activeWidth = width;
                _activeHeight = height;
            }

            if (MediaFoundationInterop.TryGetFrameRate(currentType, out var fps))
            {
                _activeFramesPerSecond = fps;
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(currentType);
        }
    }

    private IMFSourceReader CreateSourceReaderWithAccelerationFallback(
        CameraDevice camera,
        CameraVideoMode? mode,
        out object mediaSource)
    {
        if (ShouldTryDirect3D12Preview())
        {
            object? acceleratedMediaSource = null;
            try
            {
                _direct3D12 = Direct3D12DeviceManager.Create();
                StatusChanged?.Invoke(this, $"Direct3D 12 camera acceleration ready ({_direct3D12.ModeName})");
                var reader = MediaFoundationCameraDeviceFactory.CreateSourceReader(
                    camera,
                    mode,
                    _direct3D12.Manager,
                    out acceleratedMediaSource);
                mediaSource = acceleratedMediaSource;
                return reader;
            }
            catch (Exception ex)
            {
                MediaFoundationInterop.ReleaseComObject(acceleratedMediaSource);
                _direct3D12?.Dispose();
                _direct3D12 = null;
                StatusChanged?.Invoke(this, $"Direct3D 12 preview path unavailable; using CPU preview path: {ex.Message}");
            }
        }

        return MediaFoundationCameraDeviceFactory.CreateSourceReader(
            camera,
            mode,
            d3dManager: null,
            out mediaSource);
    }

    private static bool ShouldTryDirect3D12Preview()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_CAMERA_D3D12_PREVIEW");
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "force", StringComparison.OrdinalIgnoreCase);
    }
}
