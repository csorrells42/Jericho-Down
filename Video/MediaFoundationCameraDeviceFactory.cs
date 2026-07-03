using System.Runtime.InteropServices;

namespace PodcastWorkbench.Video;

internal static class MediaFoundationCameraDeviceFactory
{
    private static int _startupReferences;
    private static readonly object StartupLock = new();

    public static MediaFoundationScope Startup()
    {
        lock (StartupLock)
        {
            if (_startupReferences == 0)
            {
                MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFStartup(
                    MediaFoundationInterop.MF_VERSION,
                    MediaFoundationInterop.MFSTARTUP_FULL));
            }

            _startupReferences++;
        }

        return new MediaFoundationScope();
    }

    public static IMFSourceReader CreateSourceReader(
        string cameraName,
        CameraVideoMode? mode,
        IMFDXGIDeviceManager? d3dManager,
        out object mediaSource)
    {
        mediaSource = null!;
        var activate = FindCameraActivate(cameraName)
            ?? throw new InvalidOperationException($"Media Foundation could not find camera: {cameraName}");

        try
        {
            var sourceReaderId = typeof(IMFSourceReader).GUID;
            mediaSource = CreateMediaSource(activate, cameraName);
            var attributeResult = MediaFoundationInterop.MFCreateAttributes(out var attributes, 4);
            if (MediaFoundationInterop.Failed(attributeResult))
            {
                throw new InvalidOperationException($"Media Foundation source-reader attributes failed: 0x{attributeResult:X8}");
            }

            try
            {
                var hardwareResult = attributes.SetUINT32(
                    MediaFoundationGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
                    1);
                if (MediaFoundationInterop.Failed(hardwareResult))
                {
                    throw new InvalidOperationException($"Media Foundation hardware-transform attribute failed: 0x{hardwareResult:X8}");
                }

                if (d3dManager is not null)
                {
                    var d3dResult = attributes.SetUnknown(
                        MediaFoundationGuids.MF_SOURCE_READER_D3D_MANAGER,
                        d3dManager);
                    if (MediaFoundationInterop.Failed(d3dResult))
                    {
                        throw new InvalidOperationException($"Media Foundation D3D manager attribute failed: 0x{d3dResult:X8}");
                    }
                }
                else
                {
                    var videoProcessingResult = attributes.SetUINT32(
                        MediaFoundationGuids.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING,
                        1);
                    if (MediaFoundationInterop.Failed(videoProcessingResult))
                    {
                        throw new InvalidOperationException($"Media Foundation video-processing attribute failed: 0x{videoProcessingResult:X8}");
                    }
                }

                var readerResult = MediaFoundationInterop.MFCreateSourceReaderFromMediaSource(
                    mediaSource,
                    attributes,
                    out var reader);
                if (MediaFoundationInterop.Failed(readerResult))
                {
                    throw new InvalidOperationException($"Media Foundation source-reader creation failed: 0x{readerResult:X8}");
                }

                try
                {
                    ConfigureReader(reader, mode);
                    return reader;
                }
                catch
                {
                    MediaFoundationInterop.ReleaseComObject(reader);
                    throw;
                }
            }
            finally
            {
                MediaFoundationInterop.ReleaseComObject(attributes);
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(activate);
        }
    }

    public static object CreateMediaSource(IMFActivate activate, string cameraName)
    {
        var mediaSourceId = new Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66");
        var activateResult = activate.ActivateObject(mediaSourceId, out var activatedSource);
        if (!MediaFoundationInterop.Failed(activateResult) && activatedSource is not null)
        {
            return activatedSource;
        }

        var symbolicLink = MediaFoundationInterop.GetAllocatedString(
            activate,
            MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);
        if (string.IsNullOrWhiteSpace(symbolicLink))
        {
            throw new InvalidOperationException($"Media Foundation camera activation failed: 0x{activateResult:X8}");
        }

        MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateAttributes(out var attributes, 2));
        try
        {
            MediaFoundationInterop.ThrowIfFailed(attributes.SetGUID(
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID));
            MediaFoundationInterop.ThrowIfFailed(attributes.SetString(
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
                symbolicLink));
            var sourceResult = MediaFoundationInterop.MFCreateDeviceSource(attributes, out var mediaSource);
            if (MediaFoundationInterop.Failed(sourceResult) || mediaSource is null)
            {
                throw new InvalidOperationException($"Media Foundation device-source creation failed for {cameraName}: 0x{sourceResult:X8}");
            }

            return mediaSource;
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(attributes);
        }
    }

    public static IReadOnlyList<IMFActivate> EnumerateVideoActivates()
    {
        MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateAttributes(out var attributes, 1));
        try
        {
            MediaFoundationInterop.ThrowIfFailed(attributes.SetGUID(
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID));

            var result = MediaFoundationInterop.MFEnumDeviceSources(attributes, out var activateArray, out var count);
            MediaFoundationInterop.ThrowIfFailed(result);

            try
            {
                var devices = new List<IMFActivate>();
                for (var i = 0; i < count; i++)
                {
                    var activatePointer = Marshal.ReadIntPtr(activateArray, i * IntPtr.Size);
                    if (activatePointer != IntPtr.Zero)
                    {
                        devices.Add((IMFActivate)Marshal.GetObjectForIUnknown(activatePointer));
                        Marshal.Release(activatePointer);
                    }
                }

                return devices;
            }
            finally
            {
                Marshal.FreeCoTaskMem(activateArray);
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(attributes);
        }
    }

    public static IMFActivate? FindCameraActivate(string cameraName)
    {
        var candidates = EnumerateVideoActivates();
        IMFActivate? fallback = null;

        foreach (var activate in candidates)
        {
            var friendlyName = MediaFoundationInterop.GetAllocatedString(
                activate,
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME);
            var symbolicLink = MediaFoundationInterop.GetAllocatedString(
                activate,
                MediaFoundationGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);

            if (string.Equals(friendlyName, cameraName, StringComparison.OrdinalIgnoreCase))
            {
                ReleaseAllExcept(candidates, activate);
                return activate;
            }

            if (fallback is null
                && friendlyName?.Contains(cameraName, StringComparison.OrdinalIgnoreCase) == true)
            {
                fallback = activate;
                continue;
            }

            if (fallback is null
                && symbolicLink?.Contains(cameraName, StringComparison.OrdinalIgnoreCase) == true)
            {
                fallback = activate;
                continue;
            }

        }

        if (fallback is not null)
        {
            ReleaseAllExcept(candidates, fallback);
        }

        return fallback;
    }

    private static void ConfigureReader(IMFSourceReader reader, CameraVideoMode? mode)
    {
        var width = mode?.Width ?? 1280;
        var height = mode?.Height ?? 720;
        var fps = mode?.FramesPerSecond ?? 30d;
        var (fpsNumerator, fpsDenominator) = CreateFrameRateRatio(fps);

        MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateMediaType(out var mediaType));
        try
        {
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
                MediaFoundationGuids.MF_MT_MAJOR_TYPE,
                MediaFoundationGuids.MFMediaType_Video));
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
                MediaFoundationGuids.MF_MT_SUBTYPE,
                MediaFoundationGuids.MFVideoFormat_RGB32));
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT64(
                MediaFoundationGuids.MF_MT_FRAME_SIZE,
                MediaFoundationInterop.PackRatio(width, height)));
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT64(
                MediaFoundationGuids.MF_MT_FRAME_RATE,
                MediaFoundationInterop.PackRatio(fpsNumerator, fpsDenominator)));
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetUINT32(
                MediaFoundationGuids.MF_MT_INTERLACE_MODE,
                MediaFoundationInterop.MFVideoInterlace_Progressive));
            var result = reader.SetCurrentMediaType(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    IntPtr.Zero,
                    mediaType);
            if (MediaFoundationInterop.Failed(result))
            {
                MediaFoundationInterop.ReleaseComObject(mediaType);
                MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateMediaType(out mediaType));
                MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
                    MediaFoundationGuids.MF_MT_MAJOR_TYPE,
                    MediaFoundationGuids.MFMediaType_Video));
                MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
                    MediaFoundationGuids.MF_MT_SUBTYPE,
                    MediaFoundationGuids.MFVideoFormat_RGB32));
                var fallbackResult = reader.SetCurrentMediaType(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    IntPtr.Zero,
                    mediaType);
                if (MediaFoundationInterop.Failed(fallbackResult))
                {
                    throw new InvalidOperationException($"Media Foundation RGB32 preview type failed: 0x{result:X8}; fallback failed: 0x{fallbackResult:X8}");
                }
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(mediaType);
        }
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

    private static void ReleaseAllExcept(IReadOnlyList<IMFActivate> candidates, IMFActivate keep)
    {
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate, keep))
            {
                MediaFoundationInterop.ReleaseComObject(candidate);
            }
        }
    }

    public sealed class MediaFoundationScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (StartupLock)
            {
                if (_startupReferences <= 0)
                {
                    return;
                }

                _startupReferences--;
                if (_startupReferences == 0)
                {
                    MediaFoundationInterop.MFShutdown();
                }
            }
        }
    }
}
