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

    public bool Start(string cameraName, CameraVideoMode? mode)
    {
        Stop();

        if (!OperatingSystem.IsWindows())
        {
            StatusChanged?.Invoke(this, "Media Foundation camera capture requires Windows");
            return false;
        }

        try
        {
            _mediaFoundationScope = MediaFoundationCameraDeviceFactory.Startup();
            _reader = CreateSourceReaderWithAccelerationFallback(
                cameraName,
                mode,
                out _mediaSource);
            UpdateActiveFormat(_reader, mode);
            _cancellation = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoopAsync(_reader, _activeWidth, _activeHeight, _cancellation.Token));
            StatusChanged?.Invoke(this, $"Starting Media Foundation preview: {cameraName}");
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
        _cancellation?.Cancel();

        try
        {
            _captureTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _captureTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
        StopRecording();
        CleanupPreviewObjects();
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task CaptureLoopAsync(IMFSourceReader reader, int width, int height, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = reader.ReadSample(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out _,
                    out var streamFlags,
                    out _,
                    out var sample);

                if (MediaFoundationInterop.Failed(result))
                {
                    StatusChanged?.Invoke(this, $"Camera read failed: 0x{result:X8}");
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                if ((streamFlags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    StatusChanged?.Invoke(this, "Camera preview ended");
                    break;
                }

                if (sample is null)
                {
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
                    MediaFoundationInterop.ReleaseComObject(sample);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, ex.Message);
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
        _previousDenoiseFrame = null;
        _activeWidth = 1280;
        _activeHeight = 720;
        _activeFramesPerSecond = 30d;
        MediaFoundationInterop.ReleaseComObject(_reader);
        MediaFoundationInterop.ReleaseComObject(_mediaSource);
        _reader = null;
        _mediaSource = null;
        _direct3D12?.Dispose();
        _direct3D12 = null;
        _mediaFoundationScope?.Dispose();
        _mediaFoundationScope = null;
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
        string cameraName,
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
                    cameraName,
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
            cameraName,
            mode,
            d3dManager: null,
            out mediaSource);
    }

    private static bool ShouldTryDirect3D12Preview()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_CAMERA_D3D12_PREVIEW");
        return !string.Equals(value, "0", StringComparison.Ordinal)
            && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }
}
