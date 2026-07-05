using System.ComponentModel;
using PodcastWorkbench;
using PodcastWorkbench.Audio;
using PodcastWorkbench.Video;

var tests = new (string Name, Action Test)[]
{
    ("Video color processor leaves neutral frames unchanged", VideoColorProcessorNeutralFrame),
    ("Video color processor applies visible adjustments", VideoColorProcessorAppliesAdjustments),
    ("Video denoiser seeds first frame unchanged", VideoDenoiserSeedsFirstFrame),
    ("Video denoiser blends follow-up frames", VideoDenoiserBlendsFollowUpFrames),
    ("Video denoiser reset clears history", VideoDenoiserResetClearsHistory),
    ("File browser watcher refreshes relevant paths", FileBrowserWatcherRefreshesRelevantPaths),
    ("File browser watcher refreshes relevant renames", FileBrowserWatcherRefreshesRelevantRenames),
    ("File browser watcher ignores changed events", FileBrowserWatcherIgnoresChangedEvents),
    ("Texture preview failure cache is scoped by mode", TexturePreviewFailureCacheIsScopedByMode),
    ("Texture preview failure cache clears successful modes", TexturePreviewFailureCacheClearsSuccessfulModes),
    ("Camera catalog groups physical fallback paths", CameraCatalogGroupsPhysicalFallbackPaths),
    ("Camera catalog keeps software cameras separate", CameraCatalogKeepsSoftwareCamerasSeparate),
    ("Voice processor preserves sample count and finite output", VoiceProcessorProducesFiniteSamples),
    ("Voice telemetry snapshot is independent", VoiceTelemetrySnapshotIsIndependent),
    ("Equalizer band raises expected notifications", EqualizerBandRaisesNotifications),
    ("Audio device format display text is stable", AudioDeviceFormatDisplayText),
    ("Camera mode auto display text is stable", CameraModeAutoDisplayText)
};

var failed = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Console.Error.WriteLine($"{failed} test(s) failed.");
    return 1;
}

Console.WriteLine($"{tests.Length} tests passed.");
return 0;

static void VideoColorProcessorNeutralFrame()
{
    var frame = new byte[] { 10, 20, 30, 255, 90, 80, 70, 255 };
    var before = frame.ToArray();

    VideoFrameColorProcessor.Apply(frame, VideoFrameColorSettings.Off);

    AssertSequenceEqual(before, frame, "neutral settings should not alter pixels");
}

static void VideoColorProcessorAppliesAdjustments()
{
    var frame = new byte[] { 64, 96, 128, 12 };

    VideoFrameColorProcessor.Apply(frame, new VideoFrameColorSettings(true, 10, 15, 20, 5));

    Assert(frame[3] == 255, "processor should normalize alpha to opaque");
    Assert(frame[0] != 64 || frame[1] != 96 || frame[2] != 128, "visible adjustments should alter color channels");
}

static void VideoDenoiserSeedsFirstFrame()
{
    var denoiser = new VideoFrameDenoiser();
    var frame = new byte[] { 10, 20, 30, 40 };
    var before = frame.ToArray();

    denoiser.Apply(frame, 4);

    AssertSequenceEqual(before, frame, "first denoise frame should only seed history");
}

static void VideoDenoiserBlendsFollowUpFrames()
{
    var denoiser = new VideoFrameDenoiser();
    var seed = new byte[] { 0, 0, 0, 255 };
    var followUp = new byte[] { 120, 60, 30, 12 };

    denoiser.Apply(seed, 4);
    denoiser.Apply(followUp, 6);

    Assert(followUp[0] is > 0 and < 120, "blue channel should blend with previous frame");
    Assert(followUp[1] is > 0 and < 60, "green channel should blend with previous frame");
    Assert(followUp[2] is > 0 and < 30, "red channel should blend with previous frame");
    Assert(followUp[3] == 255, "denoiser should normalize alpha to opaque");
}

static void VideoDenoiserResetClearsHistory()
{
    var denoiser = new VideoFrameDenoiser();
    var seed = new byte[] { 0, 0, 0, 255 };
    var afterReset = new byte[] { 200, 100, 50, 7 };
    var expected = afterReset.ToArray();

    denoiser.Apply(seed, 4);
    denoiser.Reset();
    denoiser.Apply(afterReset, 4);

    AssertSequenceEqual(expected, afterReset, "reset denoiser should seed without blending");
}

static void FileBrowserWatcherRefreshesRelevantPaths()
{
    static bool IsWav(string path) => string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);

    Assert(FileBrowserWatcher.ShouldRefresh(@"C:\recordings\take.wav", IsWav), "wav files should refresh the recording browser");
    Assert(!FileBrowserWatcher.ShouldRefresh(@"C:\recordings\take.tmp", IsWav), "non-recording files should not refresh the browser");
    Assert(!FileBrowserWatcher.ShouldRefresh(null, IsWav), "missing paths should not refresh the browser");
}

static void FileBrowserWatcherRefreshesRelevantRenames()
{
    static bool IsMp4(string path) => string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase);

    Assert(FileBrowserWatcher.ShouldRefreshRename(@"C:\sessions\video_001.mp4", @"C:\sessions\video_001.tmp", IsMp4), "renaming into a video should refresh");
    Assert(FileBrowserWatcher.ShouldRefreshRename(@"C:\sessions\video_001.tmp", @"C:\sessions\video_001.mp4", IsMp4), "renaming away from a video should refresh");
    Assert(!FileBrowserWatcher.ShouldRefreshRename(@"C:\sessions\notes.txt", @"C:\sessions\notes.tmp", IsMp4), "irrelevant renames should not refresh");
}

static void FileBrowserWatcherIgnoresChangedEvents()
{
    var folder = Path.Combine(Path.GetTempPath(), "PodcastWorkbench.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var refreshCount = 0;
        using var watcher = FileBrowserWatcher.Start(
            folder,
            includeSubdirectories: false,
            NotifyFilters.FileName | NotifyFilters.CreationTime,
            path => string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase),
            () => Interlocked.Increment(ref refreshCount));

        var createdPath = Path.Combine(folder, "created.wav");
        File.WriteAllBytes(createdPath, [1, 2, 3]);
        Assert(WaitFor(() => Volatile.Read(ref refreshCount) >= 1), "creating a wav should refresh");

        var afterCreateCount = Volatile.Read(ref refreshCount);
        using (var stream = new FileStream(createdPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.WriteByte(4);
        }

        Thread.Sleep(350);
        Assert(Volatile.Read(ref refreshCount) == afterCreateCount, "changed events should not refresh the browser");

        var tempPath = Path.Combine(folder, "rename.tmp");
        var renamedPath = Path.Combine(folder, "renamed.wav");
        File.WriteAllBytes(tempPath, [5]);
        Thread.Sleep(150);
        File.Move(tempPath, renamedPath);
        Assert(WaitFor(() => Volatile.Read(ref refreshCount) > afterCreateCount), "renaming into a wav should refresh");
    }
    finally
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch
        {
        }
    }
}

static void TexturePreviewFailureCacheIsScopedByMode()
{
    var cache = new TextureNativePreviewFailureCache();
    var camera = new CameraDevice(0, "Insta360 Link 2 Pro", "Media Foundation", @"\\?\camera");
    var auto = CameraVideoMode.Auto;
    var fourK = new CameraVideoMode("3840x2160 @ 24 fps", 3840, 2160, 24d, null);

    cache.RememberFailure(camera, auto, "auto failed");

    Assert(cache.TryGetFailure(camera, auto, out var reason), "same camera/mode should reuse cached failure");
    Assert(reason == "auto failed", "cached failure should preserve reason");
    Assert(!cache.TryGetFailure(camera, fourK, out _), "different modes should get a fresh texture-native attempt");
}

static void TexturePreviewFailureCacheClearsSuccessfulModes()
{
    var cache = new TextureNativePreviewFailureCache();
    var camera = new CameraDevice(0, "Camera", "Media Foundation", string.Empty);
    var sameNamedDirectShowCamera = new CameraDevice(1, "Camera", "DirectShow", string.Empty);

    cache.RememberFailure(camera, CameraVideoMode.Auto, "failed once");
    cache.RememberFailure(sameNamedDirectShowCamera, CameraVideoMode.Auto, "directshow failed");
    cache.ForgetFailure(camera, CameraVideoMode.Auto);

    Assert(!cache.TryGetFailure(camera, CameraVideoMode.Auto, out _), "successful mode should clear its cached failure");
    Assert(cache.TryGetFailure(sameNamedDirectShowCamera, CameraVideoMode.Auto, out var reason), "camera source should be part of the cache key");
    Assert(reason == "directshow failed", "clearing one source should not clear another source");
}

static void CameraCatalogGroupsPhysicalFallbackPaths()
{
    const string physicalIdentity = @"\\?\usb#vid_2e1a&pid_4c06&mi_00#8&3818689d&0&0000";
    var mediaFoundation = new[]
    {
        new CameraDevice(0, "Insta360 Link 2 Pro", physicalIdentity + @"#{e5323777-f976-4f5b-9b55-b94699c46e44}\global", "Media Foundation")
    };
    var directShow = new[]
    {
        new CameraDevice(0, "Insta360 Link 2 Pro", physicalIdentity + @"#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global", "DirectShow")
    };

    var merged = CameraDeviceCatalog.MergeDevices(mediaFoundation, directShow);

    Assert(merged.Count == 1, "same physical camera should be shown once");
    Assert(merged[0].Source == "Media Foundation", "Media Foundation should remain the visible primary source");
    Assert(merged[0].FallbackDevice?.Source == "DirectShow", "DirectShow twin should be retained as fallback");
    Assert(merged[0].ToString() == "Insta360 Link 2 Pro", "grouped physical camera should hide backend source text");
}

static void CameraCatalogKeepsSoftwareCamerasSeparate()
{
    var mediaFoundation = Array.Empty<CameraDevice>();
    var directShow = new[]
    {
        new CameraDevice(0, "OBS Virtual Camera", @"@device:sw:{category}\{obs}", "DirectShow"),
        new CameraDevice(1, "Camera (NVIDIA Broadcast)", @"@device:sw:{category}\{broadcast}", "DirectShow")
    };

    var merged = CameraDeviceCatalog.MergeDevices(mediaFoundation, directShow);

    Assert(merged.Count == 2, "software cameras should remain separate sources");
    Assert(merged.All(camera => camera.FallbackDevice is null), "software cameras should not receive hidden fallbacks");
}

static void VoiceProcessorProducesFiniteSamples()
{
    var settings = new VoiceProcessorSettings
    {
        InputTrimDb = 3,
        HighPassEnabled = true,
        NoiseGateEnabled = true,
        CompressorEnabled = true,
        LimiterEnabled = true
    };
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    var input = new[] { -2f, -0.5f, 0f, 0.25f, 0.9f, 2f };
    var output = processor.Process(input);

    Assert(output.Length == input.Length, "processed output length should match input length");
    Assert(output.All(value => float.IsFinite(value)), "processed samples should be finite");
    Assert(output.All(value => value >= -1f && value <= 1f), "processed samples should stay in safe audio range");
}

static void VoiceTelemetrySnapshotIsIndependent()
{
    var telemetry = new VoiceProcessingTelemetry
    {
        CompressorGainReductionDb = 4.5,
        AudioLateFrameCount = 7
    };
    var snapshot = telemetry.Snapshot();

    telemetry.CompressorGainReductionDb = 0;
    telemetry.AudioLateFrameCount = 0;

    Assert(Math.Abs(snapshot.CompressorGainReductionDb - 4.5) < 0.001, "snapshot should preserve compressor reduction");
    Assert(snapshot.AudioLateFrameCount == 7, "snapshot should preserve late frame count");
}

static void EqualizerBandRaisesNotifications()
{
    var band = new EqualizerBand("1k", 1000);
    var names = new List<string?>();
    band.PropertyChanged += (_, e) => names.Add(e.PropertyName);

    band.GainDb = 3;
    band.IsEnabled = false;

    Assert(names.Contains(nameof(EqualizerBand.GainDb)), "gain change should notify GainDb");
    Assert(names.Count(name => name == nameof(EqualizerBand.DisplayValue)) >= 2, "display should update for gain and enabled changes");
    Assert(names.Contains(nameof(EqualizerBand.IsEnabled)), "enabled change should notify IsEnabled");
}

static void AudioDeviceFormatDisplayText()
{
    var format = new AudioDeviceFormat(48_000, 2, 24);

    Assert(format.ToString() == "48 kHz, 2 ch, 24-bit", "audio format display text changed unexpectedly");
}

static void CameraModeAutoDisplayText()
{
    Assert(CameraVideoMode.Auto.IsAuto, "auto mode should be marked as auto");
    Assert(CameraVideoMode.Auto.ToString() == "Auto", "auto mode display text changed unexpectedly");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertSequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    where T : IEquatable<T>
{
    Assert(expected.Count == actual.Count, $"{message}: length mismatch");
    for (var i = 0; i < expected.Count; i++)
    {
        Assert(expected[i].Equals(actual[i]), $"{message}: mismatch at index {i}");
    }
}

static bool WaitFor(Func<bool> condition, int timeoutMilliseconds = 2_500)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (DateTime.UtcNow < deadline)
    {
        if (condition())
        {
            return true;
        }

        Thread.Sleep(25);
    }

    return condition();
}
