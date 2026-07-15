using JerichoDown.Modules.Webcam;
using JerichoDown.Modules.Webcam.MediaFoundation;

namespace JerichoDown.Video;

public sealed class MediaFoundationCameraModeService
{
    private static readonly double[] CommonFrameRates = [24d, 25d, 30d, 50d, 60d];

    public bool IsAvailable => OperatingSystem.IsWindows();

    public Task<IReadOnlyList<CameraVideoMode>> GetModesAsync(CameraDevice camera, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var _ = MediaFoundationCameraDeviceFactory.Startup();
            var activate = MediaFoundationCameraDeviceFactory.FindCameraActivate(camera);
            if (activate is null)
            {
                return AddFallbackModes(camera.Name, [CameraVideoMode.Auto]);
            }

            object? mediaSource = null;
            IMFSourceReader? reader = null;
            try
            {
                mediaSource = MediaFoundationCameraDeviceFactory.CreateMediaSource(activate, camera.Name);

                MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateAttributes(out var attributes, 1));
                try
                {
                    MediaFoundationInterop.ThrowIfFailed(attributes.SetUINT32(
                        MediaFoundationGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
                        1));
                    MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateSourceReaderFromMediaSource(
                        mediaSource,
                        attributes,
                        out reader));
                }
                finally
                {
                    MediaFoundationInterop.ReleaseComObject(attributes);
                }

                var modes = new List<CameraVideoMode> { CameraVideoMode.Auto };
                for (var index = 0; index < 256; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = reader.GetNativeMediaType(
                        MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                        index,
                        out var mediaType);
                    if (result == MediaFoundationInterop.MF_E_NO_MORE_TYPES)
                    {
                        break;
                    }

                    if (MediaFoundationInterop.Failed(result))
                    {
                        continue;
                    }

                    try
                    {
                        if (TryCreateMode(mediaType, out var mode))
                        {
                            modes.Add(mode);
                        }
                    }
                    finally
                    {
                        MediaFoundationInterop.ReleaseComObject(mediaType);
                    }
                }

                return SortModes(AddFallbackModes(camera.Name, modes));
            }
            catch
            {
                return SortModes(AddFallbackModes(camera.Name, [CameraVideoMode.Auto]));
            }
            finally
            {
                MediaFoundationInterop.ReleaseComObject(reader);
                MediaFoundationInterop.ReleaseComObject(mediaSource);
                MediaFoundationInterop.ReleaseComObject(activate);
            }
        }, cancellationToken);
    }

    private static bool TryCreateMode(IMFMediaType mediaType, out CameraVideoMode mode)
    {
        mode = CameraVideoMode.Auto;
        if (MediaFoundationInterop.Failed(mediaType.GetGUID(MediaFoundationGuids.MF_MT_MAJOR_TYPE, out var majorType))
            || majorType != MediaFoundationGuids.MFMediaType_Video
            || !MediaFoundationInterop.TryGetFrameSize(mediaType, out var width, out var height)
            || !MediaFoundationInterop.TryGetFrameRate(mediaType, out var fps))
        {
            return false;
        }

        mediaType.GetGUID(MediaFoundationGuids.MF_MT_SUBTYPE, out var subtype);
        fps = NormalizeFrameRate(fps);
        var format = subtype == Guid.Empty ? null : MediaFoundationInterop.FormatSubtype(subtype);
        var label = format is null
            ? $"{width}x{height} @ {fps:0.###} fps"
            : $"{width}x{height} @ {fps:0.###} fps ({format.ToUpperInvariant()})";
        mode = new CameraVideoMode(label, width, height, fps, format);
        return true;
    }

    private static double NormalizeFrameRate(double frameRate)
    {
        foreach (var commonFrameRate in CommonFrameRates)
        {
            if (Math.Abs(frameRate - commonFrameRate) < 0.25d)
            {
                return commonFrameRate;
            }
        }

        return Math.Round(frameRate, 3);
    }

    private static IReadOnlyList<CameraVideoMode> SortModes(IReadOnlyList<CameraVideoMode> modes)
    {
        return modes
            .GroupBy(mode => $"{mode.Width}|{mode.Height}|{mode.FramesPerSecond:0.###}")
            .Select(group => group
                .OrderBy(mode => FormatPriority(mode.InputFormat))
                .ThenBy(mode => mode.InputFormat)
                .First())
            .OrderBy(mode => mode.IsAuto ? -1 : 0)
            .ThenByDescending(mode => mode.Width.GetValueOrDefault() * mode.Height.GetValueOrDefault())
            .ThenByDescending(mode => mode.Width.GetValueOrDefault())
            .ThenByDescending(mode => mode.Height.GetValueOrDefault())
            .ThenByDescending(mode => mode.FramesPerSecond.GetValueOrDefault())
            .ToList();
    }

    private static int FormatPriority(string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "mjpg" => 0,
            "mjpeg" => 0,
            "h264" => 1,
            "nv12" => 2,
            "rgb32" => 3,
            null => 4,
            _ => 5
        };
    }

    private static IReadOnlyList<CameraVideoMode> AddFallbackModes(string cameraName, IReadOnlyList<CameraVideoMode> existingModes)
    {
        if (!cameraName.Contains("insta360", StringComparison.OrdinalIgnoreCase)
            && !cameraName.Contains("link 2", StringComparison.OrdinalIgnoreCase))
        {
            return existingModes;
        }

        var modes = existingModes.ToList();
        var keys = modes.Select(CreateModeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in CreateInsta360Link2FallbackModes())
        {
            if (keys.Add(CreateModeKey(mode)))
            {
                modes.Add(mode);
            }
        }

        return modes;
    }

    private static string CreateModeKey(CameraVideoMode mode)
    {
        return $"{mode.Width}|{mode.Height}|{mode.FramesPerSecond:0.###}";
    }

    private static IEnumerable<CameraVideoMode> CreateInsta360Link2FallbackModes()
    {
        var sizes = new[]
        {
            (Width: 3840, Height: 2160, MaxFps: 30d),
            (Width: 1920, Height: 1440, MaxFps: 60d),
            (Width: 1920, Height: 1080, MaxFps: 60d),
            (Width: 1280, Height: 960, MaxFps: 60d),
            (Width: 1280, Height: 720, MaxFps: 60d)
        };

        foreach (var size in sizes)
        {
            foreach (var fps in CommonFrameRates.Where(fps => fps <= size.MaxFps))
            {
                var label = $"{size.Width}x{size.Height} @ {fps:0.###} fps";
                yield return new CameraVideoMode(label, size.Width, size.Height, fps, null);
            }
        }
    }
}
