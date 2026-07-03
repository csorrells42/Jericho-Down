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
    string Status);

public static class TextureNativeCameraRecorder
{
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
