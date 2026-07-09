using System.Collections;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using JerichoDown;
using JerichoDown.Audio;
using JerichoDown.Video;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
    ("Camera catalog groups physical fallback paths", CameraCatalogGroupsPhysicalFallbackPaths),
    ("Camera catalog keeps software cameras separate", CameraCatalogKeepsSoftwareCamerasSeparate),
    ("Voice processor preserves sample count and finite output", VoiceProcessorProducesFiniteSamples),
    ("Voice low-pass filter tames high hiss", VoiceLowPassFilterTamesHighHiss),
    ("Voice hum removal notches mains buzz", VoiceHumRemovalNotchesMainsBuzz),
    ("Voice notch filter cuts one ringing tone", VoiceNotchFilterCutsOneRingingTone),
    ("Voice parametric EQ shapes one adjustable band", VoiceParametricEqShapesOneAdjustableBand),
    ("Voice saturation adds warm harmonics safely", VoiceSaturationAddsWarmHarmonicsSafely),
    ("Voice telemetry snapshot is independent", VoiceTelemetrySnapshotIsIndependent),
    ("Equalizer band raises expected notifications", EqualizerBandRaisesNotifications),
    ("Audio device format display text is stable", AudioDeviceFormatDisplayText),
    ("Processed monitor uses low-latency buffering", ProcessedMonitorUsesLowLatencyBuffering),
    ("Input channel modes map interface lanes", InputChannelModesMapInterfaceLanes),
    ("Input channel mode falls back for mono devices", InputChannelModeFallsBackForMonoDevices),
    ("Audio recording filenames identify selected source", AudioRecordingFilenamesIdentifySelectedSource),
    ("Audio recording wave format follows selected source", AudioRecordingWaveFormatFollowsSelectedSource),
    ("Spectrum frame router maps selected mics and program output", SpectrumFrameRouterMapsSelectedMicsAndProgramOutput),
    ("Spectrum analyzer emits high-resolution bins", SpectrumAnalyzerEmitsHighResolutionBins),
    ("Feedback detector catches narrow runaway spikes", FeedbackDetectorCatchesNarrowRunawaySpikes),
    ("Mix bus processor scales and protects output", MixBusProcessorScalesAndProtectsOutput),
    ("NAudio program bus mixes one-shot mic blocks", NAudioProgramBusMixesOneShotMicBlocks),
    ("Live mix audibility gates mute and solo", LiveMixAudibilityGatesMuteAndSolo),
    ("Stereo pan provider routes mono mics across stereo bus", StereoPanProviderRoutesMonoMicsAcrossStereoBus),
    ("Audio delay line delays and resets samples", AudioDelayLineDelaysAndResetsSamples),
    ("Audio sync buffer holds target latency", AudioSyncBufferHoldsTargetLatency),
    ("Audio sync buffer resamples and trims drift", AudioSyncBufferResamplesAndTrimsDrift),
    ("Camera mode auto display text is stable", CameraModeAutoDisplayText),
    ("Enhanced karaoke LRC parses inline timings", EnhancedKaraokeLrcParsesInlineTimings),
    ("Timed karaoke word selection waits for first timestamp", TimedKaraokeWordSelectionWaitsForFirstTimestamp),
    ("Short karaoke words stay visible across close timestamps", ShortKaraokeWordsStayVisibleAcrossCloseTimestamps),
    ("Karaoke lyric cache is scoped by track file", KaraokeLyricCacheIsScopedByTrackFile),
    ("Karaoke M4A duration reads MP4 movie header", KaraokeM4aDurationReadsMovieHeader),
    ("Karaoke artist falls back to iTunes folder", KaraokeArtistFallsBackToITunesFolder),
    ("Karaoke empty lyric prompt is track aware", KaraokeEmptyLyricPromptIsTrackAware),
    ("Karaoke line grouping keeps detected lyrics readable", KaraokeLineGroupingKeepsDetectedLyricsReadable),
    ("WhisperX word JSON builds enhanced word LRC", WhisperXWordJsonBuildsEnhancedWordLrc),
    ("WhisperX character JSON keeps word timings stable", WhisperXCharacterJsonKeepsWordTimingsStable),
    ("WhisperX segment fallback estimates word timings", WhisperXSegmentFallbackEstimatesWordTimings),
    ("Demucs vocal output accepts MP3 fallback", DemucsVocalOutputAcceptsMp3Fallback)
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
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
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

static void VoiceLowPassFilterTamesHighHiss()
{
    const int sampleRate = 48_000;
    var highTone = GenerateSine(sampleRate, 12_000d, 0.5d, 1.0d);
    var voiceTone = GenerateSine(sampleRate, 1_000d, 0.5d, 1.0d);
    var highBypass = ProcessLowPassTestTone(highTone, enabled: false);
    var highFiltered = ProcessLowPassTestTone(highTone, enabled: true);
    var voiceBypass = ProcessLowPassTestTone(voiceTone, enabled: false);
    var voiceFiltered = ProcessLowPassTestTone(voiceTone, enabled: true);

    var highBypassRms = CalculateTailRms(highBypass, sampleRate / 2);
    var highFilteredRms = CalculateTailRms(highFiltered, sampleRate / 2);
    var voiceBypassRms = CalculateTailRms(voiceBypass, sampleRate / 2);
    var voiceFilteredRms = CalculateTailRms(voiceFiltered, sampleRate / 2);

    Assert(highFilteredRms < highBypassRms * 0.35d, "low-pass should significantly attenuate high hiss");
    Assert(voiceFilteredRms > voiceBypassRms * 0.70d, "low-pass should preserve normal voice-band energy");
}

static void VoiceHumRemovalNotchesMainsBuzz()
{
    const int sampleRate = 48_000;
    var humTone = GenerateSine(sampleRate, 60d, 0.5d, 2.0d);
    var harmonicTone = GenerateSine(sampleRate, 120d, 0.5d, 2.0d);
    var voiceTone = GenerateSine(sampleRate, 1_000d, 0.5d, 2.0d);
    var humBypass = ProcessHumRemovalTestTone(humTone, enabled: false);
    var humFiltered = ProcessHumRemovalTestTone(humTone, enabled: true);
    var harmonicBypass = ProcessHumRemovalTestTone(harmonicTone, enabled: false);
    var harmonicFiltered = ProcessHumRemovalTestTone(harmonicTone, enabled: true);
    var voiceBypass = ProcessHumRemovalTestTone(voiceTone, enabled: false);
    var voiceFiltered = ProcessHumRemovalTestTone(voiceTone, enabled: true);

    var start = sampleRate;
    var humBypassRms = CalculateTailRms(humBypass, start);
    var humFilteredRms = CalculateTailRms(humFiltered, start);
    var harmonicBypassRms = CalculateTailRms(harmonicBypass, start);
    var harmonicFilteredRms = CalculateTailRms(harmonicFiltered, start);
    var voiceBypassRms = CalculateTailRms(voiceBypass, start);
    var voiceFilteredRms = CalculateTailRms(voiceFiltered, start);

    Assert(humFilteredRms < humBypassRms * 0.45d, "hum removal should attenuate 60 Hz mains hum");
    Assert(harmonicFilteredRms < harmonicBypassRms * 0.65d, "hum removal should attenuate the second harmonic");
    Assert(voiceFilteredRms > voiceBypassRms * 0.85d, "hum removal should preserve normal voice-band energy");
}

static void VoiceNotchFilterCutsOneRingingTone()
{
    const int sampleRate = 48_000;
    var ringingTone = GenerateSine(sampleRate, 2_800d, 0.5d, 2.0d);
    var nearbyTone = GenerateSine(sampleRate, 2_200d, 0.5d, 2.0d);
    var bypassRing = ProcessNotchFilterTestTone(ringingTone, enabled: false);
    var notchedRing = ProcessNotchFilterTestTone(ringingTone, enabled: true);
    var bypassNearby = ProcessNotchFilterTestTone(nearbyTone, enabled: false);
    var notchedNearby = ProcessNotchFilterTestTone(nearbyTone, enabled: true);

    var start = sampleRate;
    var bypassRingRms = CalculateTailRms(bypassRing, start);
    var notchedRingRms = CalculateTailRms(notchedRing, start);
    var bypassNearbyRms = CalculateTailRms(bypassNearby, start);
    var notchedNearbyRms = CalculateTailRms(notchedNearby, start);

    Assert(notchedRingRms < bypassRingRms * 0.42d, "notch filter should cut the selected ringing frequency");
    Assert(notchedNearbyRms > bypassNearbyRms * 0.74d, "notch filter should mostly preserve nearby voice content");
    Assert(notchedRing.All(float.IsFinite), "notch filter output should stay finite");
}

static void VoiceParametricEqShapesOneAdjustableBand()
{
    const int sampleRate = 48_000;
    var centerTone = GenerateSine(sampleRate, 1_000d, 0.35d, 2.0d);
    var offBandTone = GenerateSine(sampleRate, 4_000d, 0.35d, 2.0d);
    var centerBypass = ProcessParametricEqTestTone(centerTone, enabled: false, gainDb: 0d);
    var centerBoosted = ProcessParametricEqTestTone(centerTone, enabled: true, gainDb: 6d);
    var centerCut = ProcessParametricEqTestTone(centerTone, enabled: true, gainDb: -9d);
    var offBypass = ProcessParametricEqTestTone(offBandTone, enabled: false, gainDb: 0d);
    var offBoosted = ProcessParametricEqTestTone(offBandTone, enabled: true, gainDb: 6d);

    var start = sampleRate;
    var centerBypassRms = CalculateTailRms(centerBypass, start);
    var centerBoostedRms = CalculateTailRms(centerBoosted, start);
    var centerCutRms = CalculateTailRms(centerCut, start);
    var offBypassRms = CalculateTailRms(offBypass, start);
    var offBoostedRms = CalculateTailRms(offBoosted, start);

    Assert(centerBoostedRms > centerBypassRms * 1.65d, "parametric EQ boost should raise the selected frequency");
    Assert(centerCutRms < centerBypassRms * 0.52d, "parametric EQ cut should lower the selected frequency");
    Assert(offBoostedRms > offBypassRms * 0.86d && offBoostedRms < offBypassRms * 1.14d, "parametric EQ should leave off-band content mostly unchanged");
    Assert(centerBoosted.All(float.IsFinite), "parametric EQ output should stay finite");
}

static void VoiceSaturationAddsWarmHarmonicsSafely()
{
    const int sampleRate = 48_000;
    const double fundamentalHz = 1_000d;
    var tone = GenerateSine(sampleRate, fundamentalHz, 0.45d, 2.0d);
    var bypass = ProcessSaturationTestTone(tone, enabled: false);
    var warmed = ProcessSaturationTestTone(tone, enabled: true);

    var start = sampleRate;
    var bypassFundamental = CalculateToneMagnitude(bypass, sampleRate, fundamentalHz, start);
    var warmedFundamental = CalculateToneMagnitude(warmed, sampleRate, fundamentalHz, start);
    var bypassThirdHarmonic = CalculateToneMagnitude(bypass, sampleRate, fundamentalHz * 3d, start);
    var warmedThirdHarmonic = CalculateToneMagnitude(warmed, sampleRate, fundamentalHz * 3d, start);
    var warmedPeak = warmed.Skip(start).Select(Math.Abs).Max();

    Assert(warmedThirdHarmonic > Math.Max(0.002d, bypassThirdHarmonic * 5d), "saturation should add musical harmonic content");
    Assert(warmedFundamental > bypassFundamental * 0.45d, "saturation should preserve the main voice tone");
    Assert(warmedPeak <= 0.98f, "saturation should stay below clipping before the limiter");
    Assert(warmed.All(float.IsFinite), "saturation output should stay finite");
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

static void ProcessedMonitorUsesLowLatencyBuffering()
{
    var wasapiLatency = GetPrivateStaticValue<int>(typeof(MicrophoneSpectrumService), "WasapiProcessedOutputLatencyMilliseconds");
    var waveOutLatency = GetPrivateStaticValue<int>(typeof(MicrophoneSpectrumService), "WaveOutProcessedOutputLatencyMilliseconds");
    var initialBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "InitialLiveOutputBufferedDuration");
    var targetBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "TargetLiveOutputBufferedDuration");
    var maximumBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "MaximumLiveOutputBufferedDuration");

    Assert(wasapiLatency <= 20, "WASAPI live monitor latency should stay singing-friendly");
    Assert(waveOutLatency <= 50, "WaveOut fallback latency should stay below the old speaker-safe path");
    Assert(initialBuffer <= TimeSpan.FromMilliseconds(20), "initial live monitor buffer should stay low");
    Assert(targetBuffer <= TimeSpan.FromMilliseconds(20), "target live monitor buffer should stay low");
    Assert(maximumBuffer <= TimeSpan.FromMilliseconds(70), "maximum live monitor buffer should trim latency buildup");
}

static void InputChannelModesMapInterfaceLanes()
{
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.Input1Left) == 0, "input 1 should map to channel index 0");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.Input2Right) == 1, "input 2 should map to channel index 1");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.Input10) == 9, "input 10 should map to channel index 9");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.MonoSum) is null, "mono sum should not pick one channel");
    Assert(InputChannelModeInfo.GetChannelMode(0) == InputChannelMode.Input1Left, "channel index 0 should map back to input 1");
    Assert(InputChannelModeInfo.GetChannelMode(9) == InputChannelMode.Input10, "channel index 9 should map back to input 10");
    Assert(InputChannelModeInfo.GetChannelMode(10) is null, "channel index 10 should be outside the supported strip inputs");
    Assert(InputChannelModeInfo.GetDisplayLabel(InputChannelMode.Input2Right).Contains("2", StringComparison.Ordinal), "input 2 label should be stable");
}

static void InputChannelModeFallsBackForMonoDevices()
{
    var method = typeof(EqualizerWindow).GetMethod(
        "CoerceInputChannelModeForDevice",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method is not null, "channel coercion helper should be available");

    var headset = new AudioInputDevice(0, "Mono headset", 1);
    var coerced = (InputChannelMode)method!.Invoke(null, [headset, null, InputChannelMode.Input2Right])!;

    Assert(coerced == InputChannelMode.MonoSum, "a mono headset should not keep an unavailable right-channel route");
}

static void AudioRecordingFilenamesIdentifySelectedSource()
{
    var method = typeof(EqualizerWindow).GetMethod(
        "CreateAudioRecordingFileName",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method is not null, "audio recording filename helper should be available");

    var timestamp = new DateTime(2026, 7, 9, 14, 3, 5);
    var program = (string)method!.Invoke(null, [timestamp, ProcessedRecordingSource.ProgramMix, 3])!;
    var processed = (string)method!.Invoke(null, [timestamp, ProcessedRecordingSource.SelectedMicProcessed, 3])!;
    var raw = (string)method!.Invoke(null, [timestamp, ProcessedRecordingSource.SelectedMicRawBackup, 3])!;

    Assert(program == "jericho_program_mix_2026-07-09_14-03-05.wav", "program mix recordings should be clearly named");
    Assert(processed == "jericho_mic3_processed_2026-07-09_14-03-05.wav", "selected processed mic recordings should identify the mic");
    Assert(raw == "jericho_mic3_raw_backup_2026-07-09_14-03-05.wav", "raw backup recordings should identify the mic and raw source");
}

static void AudioRecordingWaveFormatFollowsSelectedSource()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        using var service = new MicrophoneSpectrumService();
        var programPath = Path.Combine(folder, "program.wav");
        service.ConfigureProcessedRecordingSource(ProcessedRecordingSource.ProgramMix, 1);
        service.StartProcessedAudioRecording(programPath);
        service.StopProcessedAudioRecording();

        var processedPath = Path.Combine(folder, "selected_processed.wav");
        service.ConfigureProcessedRecordingSource(ProcessedRecordingSource.SelectedMicProcessed, 3);
        service.StartProcessedAudioRecording(processedPath);
        service.StopProcessedAudioRecording();

        var rawPath = Path.Combine(folder, "selected_raw.wav");
        service.ConfigureProcessedRecordingSource(ProcessedRecordingSource.SelectedMicRawBackup, 3);
        service.StartProcessedAudioRecording(rawPath);
        service.StopProcessedAudioRecording();

        AssertWaveChannelCount(programPath, 2, "program mix recording should be stereo");
        AssertWaveChannelCount(processedPath, 1, "selected processed mic recording should be mono");
        AssertWaveChannelCount(rawPath, 1, "selected raw backup recording should be mono");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static void SpectrumFrameRouterMapsSelectedMicsAndProgramOutput()
{
    var lines = Enumerable.Range(1, 10)
        .Select(channelNumber => new MicrophoneSpectrumLine(
            channelNumber,
            [channelNumber / 10d, channelNumber / 20d],
            channelNumber / 100d,
            [channelNumber / 30d, channelNumber / 40d],
            channelNumber / 200d))
        .ToArray();
    var frame = new SpectrumFrame(
        [0.91d, 0.72d],
        [0.42d, 0.21d],
        [0.5f, -0.5f],
        [0.1f, -0.1f],
        0.8d,
        0.6d,
        new VoiceProcessingTelemetry(),
        48_000,
        microphoneLines: lines);

    foreach (var channelNumber in Enumerable.Range(1, 10))
    {
        var selected = SpectrumFrameRouter.CreateSelectedMicFrame(frame, channelNumber, selectedChannelHasSource: true);
        var expected = lines[channelNumber - 1];

        AssertSequenceEqual(expected.Magnitudes, selected.Magnitudes, $"mic {channelNumber} processed graph should use its own line");
        AssertSequenceEqual(expected.RawMagnitudes, selected.RawMagnitudes, $"mic {channelNumber} raw graph should use its own line");
        Assert(Math.Abs(expected.PeakLevel - selected.PeakLevel) < 0.0001d, $"mic {channelNumber} peak should follow its own line");
        Assert(Math.Abs(expected.RawPeakLevel - selected.RawPeakLevel) < 0.0001d, $"mic {channelNumber} raw peak should follow its own line");
    }

    var output = SpectrumFrameRouter.CreateProgramOutputFrame(frame);
    AssertSequenceEqual(frame.Magnitudes, output.Magnitudes, "mixing spectrum should use final program magnitudes");
    Assert(output.MicrophoneLines.Count == 0, "mixing output frame should not carry the ten individual mic graph lines");
    Assert(output.RawMagnitudes.Length == 0, "mixing output frame should not draw a separate raw mic reference line");
}

static void SpectrumAnalyzerEmitsHighResolutionBins()
{
    var analyzer = new SpectrumAnalyzer(48_000);
    var samples = new float[8192];
    for (var i = 0; i < samples.Length; i++)
    {
        samples[i] = (float)(Math.Sin(2d * Math.PI * 440d * i / 48_000d) * 0.35d);
    }

    var analysis = analyzer.AnalyzeSamples(samples);

    Assert(analysis.Magnitudes.Length >= 1000, "spectrum graph should have enough real bins for high-resolution displays");
    Assert(analysis.Magnitudes.All(double.IsFinite), "spectrum magnitudes should stay finite");
    Assert(analysis.Magnitudes.Any(value => value > 0d), "a sine input should produce visible spectrum energy");
}

static void FeedbackDetectorCatchesNarrowRunawaySpikes()
{
    var smooth = Enumerable.Repeat(0.34d, 1024).ToArray();
    var quietResult = FeedbackDangerDetector.Analyze(smooth);

    Assert(!quietResult.IsDangerous, "smooth program spectrum should not raise feedback danger");

    var spiky = Enumerable.Repeat(0.32d, 1024).ToArray();
    var spikeIndex = FeedbackDangerDetector.FrequencyToIndex(2800d, spiky.Length);
    spiky[spikeIndex - 2] = 0.39d;
    spiky[spikeIndex - 1] = 0.48d;
    spiky[spikeIndex] = 0.86d;
    spiky[spikeIndex + 1] = 0.50d;
    spiky[spikeIndex + 2] = 0.40d;

    var danger = FeedbackDangerDetector.Analyze(spiky);

    Assert(danger.IsDangerous, "narrow, tall spectrum spike should raise feedback danger");
    Assert(danger.Score > 0.45d, "feedback score should reflect a strong spike");
    Assert(Math.Abs(danger.FrequencyHz - 2800d) < 120d, $"feedback frequency should point near the spike, found {danger.FrequencyHz:0} Hz");
    Assert(danger.SpikeRiseDb > 15d, "feedback spike should report meaningful rise over its neighborhood");
}

static void MixBusProcessorScalesAndProtectsOutput()
{
    var processor = new MixBusProcessor();
    var input = new[] { 0.5f, -0.5f, 0.25f };
    var output = new float[input.Length];

    processor.Process(input, output, new MixBusSettings(50d, false, false, -1d));

    Assert(Math.Abs(output[0] - 0.25f) < 0.0001f, "master volume should scale positive samples");
    Assert(Math.Abs(output[1] + 0.25f) < 0.0001f, "master volume should scale negative samples");
    Assert(Math.Abs(processor.LastTelemetry.PeakLevel - 0.25d) < 0.0001d, "mix bus telemetry should report processed peak level");
    Assert(processor.LastTelemetry.RmsLevel > 0d, "mix bus telemetry should report processed RMS level");

    processor.Process([1f, -1f, float.NaN], output, new MixBusSettings(200d, true, true, -1d));
    Assert(output.All(float.IsFinite), "mix bus output should stay finite");
    Assert(output.All(sample => Math.Abs(sample) <= 1f), "mix bus output should stay in audio range");
    Assert(processor.LastTelemetry.LimiterReductionDb > 0d, "mix bus telemetry should report master limiter gain reduction");
    Assert(processor.LastTelemetry.NormalizeGain > 0d, "mix bus telemetry should report normalizer gain");

    var stereo = new[] { 1f, 0f, 0f, -1f };
    output = new float[stereo.Length];
    processor.Process(stereo, output, new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Mono));

    Assert(Math.Abs(output[0] - 0.5f) < 0.0001f, "mono output mode should average left and right");
    Assert(Math.Abs(output[1] - 0.5f) < 0.0001f, "mono output mode should duplicate the averaged sample");
    Assert(Math.Abs(output[2] + 0.5f) < 0.0001f, "mono output mode should average following stereo frames");
    Assert(Math.Abs(output[3] + 0.5f) < 0.0001f, "mono output mode should duplicate following stereo frames");
    Assert(Math.Abs(processor.LastTelemetry.PeakLevel - 0.5d) < 0.0001d, "mono output telemetry should follow the duplicated mono program sample");
}

static void NAudioProgramBusMixesOneShotMicBlocks()
{
    var mic1 = new LiveMicBlockSampleProvider(48_000);
    var mic2 = new LiveMicBlockSampleProvider(48_000);
    var mic1Fader = new VolumeSampleProvider(mic1) { Volume = 0.5f };
    var mic2Fader = new VolumeSampleProvider(mic2) { Volume = 0f };
    var mixer = new MixingSampleProvider(mic1.WaveFormat)
    {
        ReadFully = true
    };

    mixer.AddMixerInput(mic1Fader);
    mixer.AddMixerInput(mic2Fader);
    mic1.SetBlock([0.4f, -0.2f, 0.1f, 0f]);
    mic2.SetBlock([1f, 1f, 1f, 1f]);

    var output = new float[6];
    var read = mixer.Read(output, 0, output.Length);

    Assert(read == output.Length, "program mixer should keep the live bus full for output routing");
    Assert(Math.Abs(output[0] - 0.2f) < 0.0001f, "mic 1 fader should scale the first sample");
    Assert(Math.Abs(output[1] + 0.1f) < 0.0001f, "mic 1 fader should scale negative samples");
    Assert(Math.Abs(output[2] - 0.05f) < 0.0001f, "mic 1 fader should scale following samples");
    Assert(Math.Abs(output[4]) < 0.0001f, "exhausted one-shot blocks should read as silence");

    mic1.SetBlock([0.2f, 0.2f]);
    mic2.SetBlock([0.2f, 0.2f]);
    mic2Fader.Volume = 1f;
    output = new float[2];
    mixer.Read(output, 0, output.Length);

    Assert(Math.Abs(output[0] - 0.3f) < 0.0001f, "program mixer should sum active mic faders");
    Assert(Math.Abs(output[1] - 0.3f) < 0.0001f, "program mixer should sum each sample frame");
}

static void LiveMixAudibilityGatesMuteAndSolo()
{
    var mic1 = new LiveMicBlockSampleProvider(48_000);
    var mic2 = new LiveMicBlockSampleProvider(48_000);
    var mic3 = new LiveMicBlockSampleProvider(48_000);
    var mic1Fader = new VolumeSampleProvider(mic1);
    var mic2Fader = new VolumeSampleProvider(mic2);
    var mic3Fader = new VolumeSampleProvider(mic3);
    var mixer = new MixingSampleProvider(mic1.WaveFormat)
    {
        ReadFully = true
    };

    mixer.AddMixerInput(mic1Fader);
    mixer.AddMixerInput(mic2Fader);
    mixer.AddMixerInput(mic3Fader);

    mic1Fader.Volume = (float)LiveMixAudibility.ResolveVolume(1d, isEnabled: true, isMuted: false, isSoloed: false, hasSolo: false);
    mic2Fader.Volume = (float)LiveMixAudibility.ResolveVolume(1d, isEnabled: true, isMuted: true, isSoloed: false, hasSolo: false);
    mic3Fader.Volume = (float)LiveMixAudibility.ResolveVolume(1d, isEnabled: true, isMuted: false, isSoloed: false, hasSolo: false);
    mic1.SetBlock([0.2f]);
    mic2.SetBlock([0.7f]);
    mic3.SetBlock([0.4f]);
    var output = new float[1];
    mixer.Read(output, 0, output.Length);

    Assert(Math.Abs(output[0] - 0.6f) < 0.0001f, "muted mics should not reach the live program bus");

    mic1Fader.Volume = (float)LiveMixAudibility.ResolveVolume(1d, isEnabled: true, isMuted: false, isSoloed: false, hasSolo: true);
    mic2Fader.Volume = (float)LiveMixAudibility.ResolveVolume(1d, isEnabled: true, isMuted: false, isSoloed: true, hasSolo: true);
    mic3Fader.Volume = (float)LiveMixAudibility.ResolveVolume(1d, isEnabled: true, isMuted: false, isSoloed: false, hasSolo: true);
    mic1.SetBlock([0.2f]);
    mic2.SetBlock([0.7f]);
    mic3.SetBlock([0.4f]);
    output[0] = 0f;
    mixer.Read(output, 0, output.Length);

    Assert(Math.Abs(output[0] - 0.7f) < 0.0001f, "solo should leave only soloed mics in the live program bus");
}

static void StereoPanProviderRoutesMonoMicsAcrossStereoBus()
{
    var mic1 = new LiveMicBlockSampleProvider(48_000);
    var mic2 = new LiveMicBlockSampleProvider(48_000);
    var left = new StereoPanSampleProvider(mic1) { Pan = -1d };
    var right = new StereoPanSampleProvider(mic2) { Pan = 1d };
    var mixer = new MixingSampleProvider(left.WaveFormat)
    {
        ReadFully = true
    };

    mixer.AddMixerInput(left);
    mixer.AddMixerInput(right);
    mic1.SetBlock([0.5f, 0.5f]);
    mic2.SetBlock([0.25f, 0.25f]);
    var output = new float[4];
    mixer.Read(output, 0, output.Length);

    Assert(Math.Abs(output[0] - 0.5f) < 0.0001f, "hard-left mic should land in the left channel");
    Assert(Math.Abs(output[1] - 0.25f) < 0.0001f, "hard-right mic should land in the right channel");
    Assert(Math.Abs(output[2] - 0.5f) < 0.0001f, "hard-left mic should stay left on following frames");
    Assert(Math.Abs(output[3] - 0.25f) < 0.0001f, "hard-right mic should stay right on following frames");

    var centerMic = new LiveMicBlockSampleProvider(48_000);
    var center = new StereoPanSampleProvider(centerMic) { Pan = 0d };
    centerMic.SetBlock([1f]);
    output = new float[2];
    center.Read(output, 0, output.Length);

    Assert(Math.Abs(output[0] - output[1]) < 0.0001f, "center pan should balance left and right");
    Assert(output[0] > 0.7f && output[0] < 0.71f, "center pan should use equal-power gain");
}

static void AudioDelayLineDelaysAndResetsSamples()
{
    var delay = new AudioDelayLine(8_000, 10d);
    var samples = new[] { 1f, 2f, 3f, 4f };

    delay.Process(samples, 0.25d);

    AssertSequenceEqual(new[] { 0f, 0f, 1f, 2f }, samples, "delay line should emit older samples after the requested delay");

    delay.Reset();
    samples = [5f, 6f];
    delay.Process(samples, 0d);

    AssertSequenceEqual(new[] { 5f, 6f }, samples, "zero delay after reset should pass samples through");
}

static void AudioSyncBufferHoldsTargetLatency()
{
    var buffer = new AudioSyncBuffer(48_000, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(40));
    var output = new float[4];

    buffer.Write([0.1f, 0.2f, 0.3f, 0.4f], 48_000);
    Assert(!buffer.ReadAligned(output), "buffer should wait until target latency is accumulated");
    Assert(output.All(sample => sample == 0f), "latency holdoff should output silence");

    buffer.Write(Enumerable.Repeat(0.5f, 500).ToArray(), 48_000);
    Assert(buffer.ReadAligned(output), "buffer should read after target latency is available");
    Assert(Math.Abs(output[0] - 0.1f) < 0.0001f, "first delayed sample should be preserved");
    Assert(buffer.UnderflowCount == 1, "underflow should be counted once");
}

static void AudioSyncBufferResamplesAndTrimsDrift()
{
    var resampleBuffer = new AudioSyncBuffer(48_000, TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
    var source = Enumerable.Range(0, 441).Select(index => index / 440f).ToArray();
    var output = new float[480];

    resampleBuffer.Write(source, 44_100);
    Assert(resampleBuffer.ReadAligned(output), "resampled buffer should provide the requested output");
    Assert(Math.Abs(output[0]) < 0.0001f, "resampled output should start at the first source sample");
    Assert(output[^1] > 0.99f, "resampled output should reach the end of the source ramp");
    Assert(output.All(float.IsFinite), "resampled output should stay finite");

    var driftBuffer = new AudioSyncBuffer(1_000, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
    driftBuffer.Write(Enumerable.Repeat(0.25f, 100).ToArray(), 1_000);
    var beforeTrimCount = driftBuffer.BufferedSamples;
    Assert(driftBuffer.ReadAligned(new float[5]), "drift buffer should still produce output after trimming");
    Assert(driftBuffer.DriftTrimCount > 0, "excess buffered audio should be trimmed as drift");
    Assert(driftBuffer.BufferedSamples < beforeTrimCount, "drift trim should reduce buffered audio");
}

static void CameraModeAutoDisplayText()
{
    Assert(CameraVideoMode.Auto.IsAuto, "auto mode should be marked as auto");
    Assert(CameraVideoMode.Auto.ToString() == "Auto", "auto mode display text changed unexpectedly");
}

static void EnhancedKaraokeLrcParsesInlineTimings()
{
    const string lrc = "[00:01.00] <00:01.20>beau<00:01.50>ti<00:01.70>ful <00:02.10>song";
    var lines = ParseKaraokeLines(lrc);

    Assert(lines.Count == 1, "one enhanced karaoke line should be parsed");
    Assert(GetProperty<string>(lines[0], "Text") == "beautiful song", "inline timestamps should not leak into display text");

    var tokens = GetProperty<IEnumerable>(lines[0], "Tokens").Cast<object>().ToList();
    Assert(tokens.Count == 5, "enhanced line should preserve per-syllable tokens and the word space");
    Assert(GetProperty<string>(tokens[0], "Text") == "beau", "first syllable text changed");
    Assert(GetProperty<string>(tokens[1], "Text") == "ti", "second syllable text changed");
    Assert(GetProperty<string>(tokens[2], "Text") == "ful", "third syllable text changed");
    Assert(GetProperty<string>(tokens[3], "Text") == " ", "word spacing should remain a non-singing token");
    Assert(GetProperty<string>(tokens[4], "Text") == "song", "next word text changed");
    Assert(GetProperty<bool>(tokens[0], "IsSingable"), "syllable tokens should be singable");
    Assert(!GetProperty<bool>(tokens[3], "IsSingable"), "spacing should not be singable");
    Assert(GetProperty<TimeSpan?>(tokens[0], "Start") == ApplyKaraokeDisplayLead(1_200), "first syllable timestamp changed");
    Assert(GetProperty<TimeSpan?>(tokens[1], "Start") == ApplyKaraokeDisplayLead(1_500), "second syllable timestamp changed");
    Assert(GetProperty<TimeSpan?>(tokens[2], "Start") == ApplyKaraokeDisplayLead(1_700), "third syllable timestamp changed");
    Assert(GetProperty<TimeSpan?>(tokens[4], "Start") == ApplyKaraokeDisplayLead(2_100), "next word timestamp changed");
}

static void TimedKaraokeWordSelectionWaitsForFirstTimestamp()
{
    const string lrc = "[00:01.00] <00:01.20>beau<00:01.50>ti<00:01.70>ful <00:02.10>song";
    var line = CreateKaraokeLineItem(ParseKaraokeLines(lrc)[0], TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));

    Assert(GetActiveKaraokeTokenIndex(line, ApplyKaraokeDisplayLead(1_200) - TimeSpan.FromMilliseconds(1)) == -1, "highlight should wait for the first syllable timestamp");
    Assert(GetActiveKaraokeTokenIndex(line, ApplyKaraokeDisplayLead(1_200)) == 0, "first syllable should activate at its timestamp");
    Assert(GetActiveKaraokeTokenIndex(line, ApplyKaraokeDisplayLead(1_520)) == 1, "second syllable should activate at its timestamp");
    Assert(GetActiveKaraokeTokenIndex(line, ApplyKaraokeDisplayLead(1_720)) == 2, "third syllable should activate at its timestamp");
    Assert(GetActiveKaraokeTokenIndex(line, ApplyKaraokeDisplayLead(2_120)) == 4, "next word should activate at its timestamp");
}

static void ShortKaraokeWordsStayVisibleAcrossCloseTimestamps()
{
    const string lrc = "[00:02.00] <00:02.00>I <00:02.05>am <00:02.12>here";
    var line = CreateKaraokeLineItem(ParseKaraokeLines(lrc)[0], TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    var firstWordStart = ApplyKaraokeDisplayLead(2_000);
    var secondWordStart = ApplyKaraokeDisplayLead(2_050);
    var shortWordHold = GetPrivateStaticValue<TimeSpan>(typeof(EqualizerWindow), "KaraokeShortWordDisplayHold");

    Assert(GetActiveKaraokeTokenIndex(line, firstWordStart + TimeSpan.FromMilliseconds(130)) == 0, "a close following timestamp should not skip a one-letter word");
    Assert(GetActiveKaraokeTokenIndex(line, firstWordStart + shortWordHold + TimeSpan.FromMilliseconds(5)) == 2, "second short word should activate after the first has been visible");
    Assert(GetActiveKaraokeTokenIndex(line, secondWordStart + shortWordHold + TimeSpan.FromMilliseconds(5)) == 4, "longer following word should activate after the second short word has been visible");
}

static void KaraokeLyricCacheIsScopedByTrackFile()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var firstTrack = Path.Combine(folder, "first.wav");
        var secondTrack = Path.Combine(folder, "second.wav");
        File.WriteAllBytes(firstTrack, [1, 2, 3]);
        File.WriteAllBytes(secondTrack, [1, 2, 3]);

        var firstCachePath = GetKaraokeLyricCachePath(firstTrack);
        var secondCachePath = GetKaraokeLyricCachePath(secondTrack);

        Assert(!string.IsNullOrWhiteSpace(firstCachePath), "first track should produce a lyric cache path");
        Assert(!string.IsNullOrWhiteSpace(secondCachePath), "second track should produce a lyric cache path");
        Assert(!string.Equals(firstCachePath, secondCachePath, StringComparison.OrdinalIgnoreCase), "different tracks should not share lyric cache files");

        Assert(SaveCachedKaraokeLyricsForTrack(firstTrack, "[00:00.00] alpha"), "valid track lyrics should save to cache");
        Assert(File.Exists(firstCachePath), "saved lyric cache file should exist");
        Assert(File.ReadAllText(firstCachePath!).Contains("alpha", StringComparison.Ordinal), "saved lyric cache should preserve text");

        var staleCachePath = firstCachePath;
        Thread.Sleep(20);
        File.WriteAllBytes(firstTrack, [1, 2, 3, 4]);
        var changedCachePath = GetKaraokeLyricCachePath(firstTrack);
        Assert(!string.Equals(staleCachePath, changedCachePath, StringComparison.OrdinalIgnoreCase), "track edits should not reuse stale lyric cache files");
        Assert(!SaveCachedKaraokeLyricsForTrack(Path.Combine(folder, "missing.wav"), "[00:00.00] beta"), "missing tracks should not be marked as cached");
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

static void KaraokeM4aDurationReadsMovieHeader()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var path = Path.Combine(folder, "sample.m4a");
        File.WriteAllBytes(path, CreateMinimalM4aWithDuration(44100, 44100 * 187));

        Assert(TryReadKaraokeTrackDuration(path, out var duration), "synthetic M4A should expose duration from mvhd");
        Assert(Math.Abs((duration - TimeSpan.FromSeconds(187)).TotalMilliseconds) < 1d, "M4A movie header duration should be decoded");
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

static void KaraokeArtistFallsBackToITunesFolder()
{
    var path = Path.Combine(
        @"C:\",
        "Users",
        "clsor",
        "Music",
        "iTunes",
        "iTunes Media",
        "Music",
        "Chris Tomlin",
        "Love Ran Red",
        "03 At the Cross.m4a");

    var artist = (string)InvokeEqualizerWindowPrivateStatic("InferKaraokeArtistFromPath", path);

    Assert(artist == "Chris Tomlin", "iTunes artist folder should populate karaoke author");
}

static void KaraokeEmptyLyricPromptIsTrackAware()
{
    var noTrackPrompt = (string)InvokeEqualizerWindowPrivateStaticWithArgs("GetKaraokeEmptyLyricDisplayText", [null]);
    var loadedTrackPrompt = (string)InvokeEqualizerWindowPrivateStatic("GetKaraokeEmptyLyricDisplayText", @"C:\Music\Artist\Album\Song.m4a");

    Assert(noTrackPrompt.Contains("Load a backing track", StringComparison.Ordinal), "empty karaoke prompt should guide users before a track is loaded");
    Assert(loadedTrackPrompt.Contains("No lyrics loaded", StringComparison.Ordinal), "empty karaoke prompt should change once a backing track is loaded");
    Assert(!loadedTrackPrompt.Contains("Load a backing track", StringComparison.Ordinal), "loaded-track prompt should not keep stale backing-track guidance");
}

static void KaraokeLineGroupingKeepsDetectedLyricsReadable()
{
    const string json = """
        {
          "segments": [
            {
              "start": 1.0,
              "end": 9.5,
              "text": "synthetic line grouping sample",
              "words": [
                { "word": "When", "start": 1.0, "end": 1.3 },
                { "word": "bright", "start": 1.35, "end": 1.75 },
                { "word": "morning", "start": 1.8, "end": 2.2 },
                { "word": "comes", "start": 2.25, "end": 2.55 },
                { "word": "we", "start": 3.1, "end": 3.25 },
                { "word": "all", "start": 3.3, "end": 3.55 },
                { "word": "stand", "start": 3.6, "end": 3.95 },
                { "word": "ready", "start": 4.0, "end": 4.35 },
                { "word": "and", "start": 4.9, "end": 5.05 },
                { "word": "sing", "start": 5.1, "end": 5.45 },
                { "word": "another", "start": 5.5, "end": 5.95 },
                { "word": "gentle", "start": 6.0, "end": 6.4 },
                { "word": "chorus", "start": 6.45, "end": 6.85 },
                { "word": "for", "start": 7.4, "end": 7.6 },
                { "word": "everyone", "start": 7.65, "end": 8.1 },
                { "word": "here", "start": 8.15, "end": 8.45 },
                { "word": "tonight", "start": 8.5, "end": 9.0 }
              ]
            }
          ]
        }
        """;

    var lrc = BuildEnhancedKaraokeLrcFromJson(json, out var timedWordCount);
    var displayLines = lrc
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.StartsWith("[00:", StringComparison.Ordinal))
        .Select(StripKaraokeTimingMarkersForTest)
        .ToList();

    Assert(timedWordCount == 17, "all synthetic words should be timed");
    Assert(displayLines.Count >= 3, "long detected lyric runs should split into readable karaoke lines");
    Assert(displayLines.All(line => line.Length <= 38), "generated lyric lines should stay inside the display line target");
    Assert(displayLines.All(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8), "generated lyric lines should avoid dense word piles");
}

static void WhisperXWordJsonBuildsEnhancedWordLrc()
{
    const string json = """
        {
          "segments": [
            {
              "start": 1.0,
              "end": 2.6,
              "text": "Beautiful song",
              "words": [
                { "word": "Beautiful", "start": 1.0, "end": 2.0 },
                { "word": "song", "start": 2.1, "end": 2.6 }
              ]
            }
          ]
        }
        """;

    var lrc = BuildEnhancedKaraokeLrcFromJson(json, out var timedWordCount);

    Assert(timedWordCount == 2, "Beautiful song should generate two timed word tokens");
    Assert(lrc.Contains("[re:Demucs vocals + WhisperX word alignment]", StringComparison.Ordinal), "LRC should identify the pro alignment source");
    Assert(lrc.Contains("<00:01.00>Beautiful", StringComparison.Ordinal), "first word should keep WhisperX word start timing");
    Assert(lrc.Contains("<00:02.10>song", StringComparison.Ordinal), "next word should keep its own WhisperX start timing");

    var parsedLine = ParseKaraokeLines(lrc).Single(line => GetProperty<string>(line, "Text") == "Beautiful song");
    var tokens = GetProperty<IEnumerable>(parsedLine, "Tokens").Cast<object>().ToList();
    Assert(tokens.Count(token => GetProperty<bool>(token, "IsSingable")) == 2, "generated LRC should parse back into two singable display tokens");
}

static void WhisperXCharacterJsonKeepsWordTimingsStable()
{
    const string json = """
        {
          "segments": [
            {
              "start": 1.0,
              "end": 2.6,
              "text": "Beautiful song",
              "words": [
                { "word": "Beautiful", "start": 1.0, "end": 2.0 },
                { "word": "song", "start": 2.1, "end": 2.6 }
              ],
              "chars": [
                { "char": "B", "start": 1.0, "end": 1.1 },
                { "char": "e", "start": 1.1, "end": 1.2 },
                { "char": "a", "start": 1.2, "end": 1.3 },
                { "char": "u", "start": 1.3, "end": 1.4 },
                { "char": "t", "start": 1.4, "end": 1.5 },
                { "char": "i", "start": 1.5, "end": 1.6 },
                { "char": "f", "start": 1.6, "end": 1.7 },
                { "char": "u", "start": 1.7, "end": 1.8 },
                { "char": "l", "start": 1.8, "end": 1.9 },
                { "char": " ", "start": 2.0, "end": 2.1 },
                { "char": "s", "start": 2.1, "end": 2.2 },
                { "char": "o", "start": 2.2, "end": 2.3 },
                { "char": "n", "start": 2.3, "end": 2.4 },
                { "char": "g", "start": 2.4, "end": 2.5 }
              ]
            }
          ]
        }
        """;

    var lrc = BuildEnhancedKaraokeLrcFromJson(json, out var timedWordCount);

    Assert(timedWordCount == 2, "character-rich WhisperX output should still produce stable word-level tokens");
    Assert(lrc.Contains("<00:01.00>Beautiful", StringComparison.Ordinal), "first word should keep WhisperX word timing");
    Assert(lrc.Contains("<00:02.10>song", StringComparison.Ordinal), "second word should keep WhisperX word timing");
}

static void WhisperXSegmentFallbackEstimatesWordTimings()
{
    const string json = """
        {
          "segments": [
            { "start": 1.0, "end": 3.0, "text": "hello bright world" }
          ]
        }
        """;

    var words = ReadWhisperXWordsFromJson(json);

    Assert(words.Count == 3, "segment-only WhisperX output should split into estimated words");
    Assert(GetProperty<string>(words[0], "Text") == "hello", "first estimated word changed");
    Assert(GetProperty<string>(words[1], "Text") == "bright", "second estimated word changed");
    Assert(GetProperty<string>(words[2], "Text") == "world", "third estimated word changed");
    Assert(GetProperty<TimeSpan>(words[0], "Start") == TimeSpan.FromSeconds(1), "segment fallback should keep segment start");
    Assert(GetProperty<TimeSpan>(words[0], "End") <= GetProperty<TimeSpan>(words[1], "Start"), "estimated words should be monotonic");
    Assert(GetProperty<TimeSpan>(words[1], "End") <= GetProperty<TimeSpan>(words[2], "Start"), "estimated words should stay ordered");
    Assert(GetProperty<TimeSpan>(words[2], "End") == TimeSpan.FromSeconds(3), "segment fallback should keep segment end");
}

static void DemucsVocalOutputAcceptsMp3Fallback()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var nested = Path.Combine(folder, "htdemucs", "song");
        Directory.CreateDirectory(nested);
        var mp3Path = Path.Combine(nested, "vocals.mp3");
        File.WriteAllBytes(mp3Path, [1, 2, 3]);

        var detectedPath = (string?)InvokeEqualizerWindowPrivateStatic("FindDemucsVocalOutput", folder);

        Assert(string.Equals(detectedPath, mp3Path, StringComparison.OrdinalIgnoreCase), "Demucs MP3 fallback vocals should be detected");
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

static List<object> ParseKaraokeLines(string text)
{
    var parsed = InvokeEqualizerWindowPrivateStatic("ParseKaraokeTimedLyrics", text);
    return ((IEnumerable)parsed).Cast<object>().ToList();
}

static List<object> ReadWhisperXWordsFromJson(string json)
{
    return ((IEnumerable)ReadWhisperXWordsObjectFromJson(json)).Cast<object>().ToList();
}

static object ReadWhisperXWordsObjectFromJson(string json)
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "whisperx.json");
    try
    {
        File.WriteAllText(path, json);
        return InvokeEqualizerWindowPrivateStatic("ReadWhisperXWords", path);
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

static string BuildEnhancedKaraokeLrcFromJson(string json, out int syllableCount)
{
    var words = ReadWhisperXWordsObjectFromJson(json);
    var lines = InvokeEqualizerWindowPrivateStatic("GroupKaraokeWordsIntoLines", words);
    var args = new object?[] { lines, 0 };
    var lrc = (string)InvokeEqualizerWindowPrivateStaticWithArgs("BuildEnhancedKaraokeLrc", args);
    syllableCount = (int)args[1]!;
    return lrc;
}

static string StripKaraokeTimingMarkersForTest(string lrcLine)
{
    var builder = new StringBuilder(lrcLine.Length);
    var inTimingMarker = false;
    foreach (var character in lrcLine)
    {
        if (character is '[' or '<')
        {
            inTimingMarker = true;
            continue;
        }

        if (inTimingMarker)
        {
            if (character is ']' or '>')
            {
                inTimingMarker = false;
            }

            continue;
        }

        builder.Append(character);
    }

    return builder.ToString().Trim();
}

static object CreateKaraokeLineItem(object timedLine, TimeSpan start, TimeSpan end)
{
    var lineType = typeof(EqualizerWindow).GetNestedType("KaraokeLyricLineItem", BindingFlags.NonPublic);
    if (lineType is null)
    {
        throw new InvalidOperationException("karaoke lyric line type was not found");
    }

    return Activator.CreateInstance(
        lineType,
        GetProperty<string>(timedLine, "Text"),
        start,
        end,
        GetProperty<object>(timedLine, "Tokens"))!;
}

static int GetActiveKaraokeTokenIndex(object line, TimeSpan position)
{
    return (int)InvokeEqualizerWindowPrivateStatic("GetActiveKaraokeTokenIndex", line, position);
}

static TimeSpan ApplyKaraokeDisplayLead(int rawMilliseconds)
{
    var rawTimestamp = TimeSpan.FromMilliseconds(rawMilliseconds);
    var lead = GetPrivateStaticValue<TimeSpan>(typeof(EqualizerWindow), "KaraokeLyricDisplayLead");
    return rawTimestamp > lead ? rawTimestamp - lead : TimeSpan.Zero;
}

static string? GetKaraokeLyricCachePath(string trackPath)
{
    return (string?)InvokeEqualizerWindowPrivateStatic("GetKaraokeLyricCachePath", trackPath);
}

static bool SaveCachedKaraokeLyricsForTrack(string trackPath, string lyrics)
{
    return (bool)InvokeEqualizerWindowPrivateStatic("SaveCachedKaraokeLyricsForTrack", trackPath, lyrics);
}

static bool TryReadKaraokeTrackDuration(string path, out TimeSpan duration)
{
    var readerType = typeof(EqualizerWindow).GetNestedType("KaraokeTrackAudioReader", BindingFlags.NonPublic);
    if (readerType is null)
    {
        throw new InvalidOperationException("karaoke track audio reader type was not found");
    }

    var args = new object?[] { path, TimeSpan.Zero };
    var method = readerType.GetMethod("TryReadDuration", BindingFlags.Public | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("KaraokeTrackAudioReader.TryReadDuration was not found");
    }

    var result = (bool)method.Invoke(null, args)!;
    duration = (TimeSpan)args[1]!;
    return result;
}

static byte[] CreateMinimalM4aWithDuration(uint timescale, uint duration)
{
    using var stream = new MemoryStream();
    WriteBox(stream, "ftyp", writer =>
    {
        writer.Write("M4A "u8);
        writer.Write(new byte[] { 0, 0, 0, 0 });
        writer.Write("M4A "u8);
        writer.Write("mp42"u8);
    });
    WriteBox(stream, "moov", writer =>
    {
        WriteBox(writer, "mvhd", movieHeader =>
        {
            movieHeader.WriteByte(0);
            movieHeader.Write([0, 0, 0]);
            WriteUInt32BigEndian(movieHeader, 0);
            WriteUInt32BigEndian(movieHeader, 0);
            WriteUInt32BigEndian(movieHeader, timescale);
            WriteUInt32BigEndian(movieHeader, duration);
        });
    });

    return stream.ToArray();
}

static void WriteBox(Stream stream, string type, Action<Stream> writePayload)
{
    var start = stream.Position;
    WriteUInt32BigEndian(stream, 0);
    stream.Write(Encoding.ASCII.GetBytes(type));
    writePayload(stream);
    var end = stream.Position;
    stream.Position = start;
    WriteUInt32BigEndian(stream, checked((uint)(end - start)));
    stream.Position = end;
}

static void WriteUInt32BigEndian(Stream stream, uint value)
{
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    stream.Write(buffer);
}

static object InvokeEqualizerWindowPrivateStatic(string methodName, params object?[] args)
{
    return InvokeEqualizerWindowPrivateStaticWithArgs(methodName, args);
}

static object InvokeEqualizerWindowPrivateStaticWithArgs(string methodName, object?[] args)
{
    var method = typeof(EqualizerWindow).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException($"EqualizerWindow.{methodName} was not found");
    }

    return method.Invoke(null, args) ?? throw new InvalidOperationException($"EqualizerWindow.{methodName} returned null");
}

static T GetProperty<T>(object target, string name)
{
    var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property is null)
    {
        throw new InvalidOperationException($"{target.GetType().Name}.{name} was not found");
    }

    return (T)property.GetValue(target)!;
}

static T GetPrivateStaticValue<T>(Type type, string name)
{
    var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
    if (field is null)
    {
        throw new InvalidOperationException($"{type.Name}.{name} was not found");
    }

    return field.IsLiteral
        ? (T)field.GetRawConstantValue()!
        : (T)field.GetValue(null)!;
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

static void AssertWaveChannelCount(string path, int expectedChannels, string message)
{
    using var reader = new WaveFileReader(path);
    Assert(reader.WaveFormat.Channels == expectedChannels, $"{message}: found {reader.WaveFormat.Channels} channels");
}

static float[] GenerateSine(int sampleRate, double frequencyHz, double amplitude, double durationSeconds)
{
    var sampleCount = Math.Max(1, (int)(sampleRate * durationSeconds));
    var samples = new float[sampleCount];
    for (var i = 0; i < samples.Length; i++)
    {
        samples[i] = (float)(Math.Sin(2d * Math.PI * frequencyHz * i / sampleRate) * amplitude);
    }

    return samples;
}

static float[] ProcessLowPassTestTone(float[] samples, bool enabled)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        LowPassEnabled = enabled,
        LowPassFrequencyHz = 4_000,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        DeEsserEnabled = false,
        PresenceEnhancerEnabled = false,
        LimiterEnabled = false,
        MakeupGainDb = 0
    };
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessHumRemovalTestTone(float[] samples, bool enabled)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        HumRemovalEnabled = enabled,
        HumRemovalFrequencyHz = 60,
        LowPassEnabled = false,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        DeEsserEnabled = false,
        PresenceEnhancerEnabled = false,
        LimiterEnabled = false,
        MakeupGainDb = 0
    };
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessNotchFilterTestTone(float[] samples, bool enabled)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        HumRemovalEnabled = false,
        NotchFilterEnabled = enabled,
        NotchFilterFrequencyHz = 2_800,
        NotchFilterDepthDb = 30,
        NotchFilterQ = 18,
        LowPassEnabled = false,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        DeEsserEnabled = false,
        PresenceEnhancerEnabled = false,
        SaturationEnabled = false,
        LimiterEnabled = false,
        MakeupGainDb = 0
    };
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessParametricEqTestTone(float[] samples, bool enabled, double gainDb)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        HumRemovalEnabled = false,
        NotchFilterEnabled = false,
        ParametricEqEnabled = enabled,
        ParametricEqFrequencyHz = 1_000,
        ParametricEqGainDb = gainDb,
        ParametricEqQ = 2.2,
        LowPassEnabled = false,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        DeEsserEnabled = false,
        PresenceEnhancerEnabled = false,
        SaturationEnabled = false,
        LimiterEnabled = false,
        MakeupGainDb = 0
    };
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessSaturationTestTone(float[] samples, bool enabled)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        HumRemovalEnabled = false,
        LowPassEnabled = false,
        SaturationEnabled = enabled,
        SaturationAmount = 8,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        DeEsserEnabled = false,
        PresenceEnhancerEnabled = false,
        LimiterEnabled = false,
        MakeupGainDb = 0
    };
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static double CalculateToneMagnitude(IReadOnlyList<float> samples, int sampleRate, double frequencyHz, int startIndex)
{
    var start = Math.Clamp(startIndex, 0, samples.Count);
    var sine = 0d;
    var cosine = 0d;
    var count = 0;
    for (var i = start; i < samples.Count; i++)
    {
        var angle = 2d * Math.PI * frequencyHz * i / sampleRate;
        sine += samples[i] * Math.Sin(angle);
        cosine += samples[i] * Math.Cos(angle);
        count++;
    }

    return count == 0 ? 0d : 2d * Math.Sqrt(sine * sine + cosine * cosine) / count;
}

static double CalculateTailRms(IReadOnlyList<float> samples, int startIndex)
{
    var start = Math.Clamp(startIndex, 0, samples.Count);
    var sum = 0d;
    var count = 0;
    for (var i = start; i < samples.Count; i++)
    {
        sum += samples[i] * samples[i];
        count++;
    }

    return count == 0 ? 0d : Math.Sqrt(sum / count);
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
