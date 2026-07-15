using System.Collections;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using JerichoDown;
using JerichoDown.Audio;
using JerichoDown.Modules.Audio.Asio;
using JerichoDown.Modules.Midi;
using JerichoDown.Modules.Webcam;
using JerichoDown.Modules.Webcam.Dx12;
using JerichoDown.Modules.Visualization.Dx12;
using NAudio.Midi;
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
    ("App storage uses LocalAppData and rotates diagnostics", AppStorageUsesLocalAppDataAndRotatesDiagnostics),
    ("App generated files use atomic writes", AppGeneratedFilesUseAtomicWrites),
    ("Recording deletes are path bounded", RecordingDeletesArePathBounded),
    ("Karaoke AI tools use structured process arguments", KaraokeAiToolsUseStructuredProcessArguments),
    ("Camera catalog groups physical fallback paths", CameraCatalogGroupsPhysicalFallbackPaths),
    ("Camera catalog keeps software cameras separate", CameraCatalogKeepsSoftwareCamerasSeparate),
    ("Voice processor preserves sample count and finite output", VoiceProcessorProducesFiniteSamples),
    ("Voice low-pass filter tames high hiss", VoiceLowPassFilterTamesHighHiss),
    ("Voice hum removal notches mains buzz", VoiceHumRemovalNotchesMainsBuzz),
    ("Voice notch filter cuts one ringing tone", VoiceNotchFilterCutsOneRingingTone),
    ("Voice parametric EQ shapes one adjustable band", VoiceParametricEqShapesOneAdjustableBand),
    ("Voice shelf EQ shapes low body and high air", VoiceShelfEqShapesLowBodyAndHighAir),
    ("DSP verification report proves custom EQ/DSP claims", DspVerificationReportProvesCustomDspClaims),
    ("NAudio BiQuad rack exposes every EQ shape", NAudioBiQuadRackExposesEveryEqShape),
    ("NAudio pitch shift moves tone frequency", NAudioPitchShiftMovesToneFrequency),
    ("NAudio convolution adds generated impulse tail", NAudioConvolutionAddsGeneratedImpulseTail),
    ("NAudio envelope generator shapes attack", NAudioEnvelopeGeneratorShapesAttack),
    ("NAudio DMO effect chain exposes DirectSound effects", NAudioDmoEffectChainExposesDirectSoundEffects),
    ("DSP screen separates custom and NAudio families", DspScreenSeparatesCustomAndNaudioFamilies),
    ("NAudio DMO effect chain processes safely", NAudioDmoEffectChainProcessesSafely),
    ("NAudio file analyzer reports recording quality details", NAudioFileAnalyzerReportsRecordingQualityDetails),
    ("NAudio MIDI support exposes input output and file features", NAudioMidiSupportExposesInputOutputAndFileFeatures),
    ("NAudio MIDI message utilities clamp and parse safely", NAudioMidiMessageUtilitiesClampAndParseSafely),
    ("MIDI control mappings match incoming channel messages", MidiControlMappingsMatchIncomingChannelMessages),
    ("MIDI control mapping trigger state is edge gated", MidiControlMappingTriggerStateIsEdgeGated),
    ("MIDI sequence playback plan schedules tempo aware events", MidiSequencePlaybackPlanSchedulesTempoAwareEvents),
    ("SoundFont sample preview copies PCM slices safely", SoundFontSamplePreviewCopiesPcmSlicesSafely),
    ("Voice breath reducer tames airy breath noise", VoiceBreathReducerTamesAiryBreathNoise),
    ("Voice saturation adds warm harmonics safely", VoiceSaturationAddsWarmHarmonicsSafely),
    ("Voice telemetry snapshot is independent", VoiceTelemetrySnapshotIsIndependent),
    ("Equalizer band raises expected notifications", EqualizerBandRaisesNotifications),
    ("EQ screen binds every voice processor setting", EqScreenBindsEveryVoiceProcessorSetting),
    ("Main menu exposes global device and help actions", MainMenuExposesGlobalDeviceAndHelpActions),
    ("Module readmes define ownership", ModuleReadmesDefineOwnership),
    ("Podcast session playback prefers DX12 file renderer", PodcastSessionPlaybackPrefersDx12FileRenderer),
    ("Camera denoise stays on DX12 preview paths", CameraDenoiseStaysOnDx12PreviewPaths),
    ("MIDI tab is opt-in and ordered after Karaoke", MidiTabIsOptInAndOrderedAfterKaraoke),
    ("Voice processor uses every DSP setting", VoiceProcessorUsesEveryDspSetting),
    ("Audio device format display text is stable", AudioDeviceFormatDisplayText),
    ("Audio device diagnostics names selected device risks", AudioDeviceDiagnosticsNamesSelectedDeviceRisks),
    ("Audio stream restart failures back off", AudioStreamRestartFailuresBackOff),
    ("Processed monitor uses stability-first buffering", ProcessedMonitorUsesStabilityFirstBuffering),
    ("Processed output routing prefers WASAPI before WaveOut", ProcessedOutputRoutingPrefersWasapiBeforeWaveOut),
    ("WASAPI expert output settings are persisted and routed", WasapiExpertOutputSettingsArePersistedAndRouted),
    ("ASIO output routing is opt-in", AsioOutputRoutingIsOptIn),
    ("ASIO control panel rejects non-ASIO endpoints", AsioControlPanelRejectsNonAsioEndpoints),
    ("ASIO settings menu prefers selected and installed drivers", AsioSettingsMenuPrefersSelectedAndInstalledDrivers),
    ("ASIO callback test exposes driver modes", AsioCallbackTestExposesDriverModes),
    ("ASIO input devices carry endpoint identity", AsioInputDevicesCarryEndpointIdentity),
    ("ASIO input selections restore by endpoint", AsioInputSelectionsRestoreByEndpoint),
    ("ASIO restart path preserves endpoint identity", AsioRestartPathPreservesEndpointIdentity),
    ("ASIO input startup avoids pre-open probe", AsioInputStartupAvoidsPreOpenProbe),
    ("ASIO STA dispatcher pumps Windows messages", AsioStaDispatcherPumpsWindowsMessages),
    ("ASIO no-callback state clears stale graphs", AsioNoCallbackStateClearsStaleGraphs),
    ("ASIO input capture uses record-only live mode", AsioInputCaptureUsesRecordOnlyLiveMode),
    ("ASIO primary capture holds auxiliary inputs", AsioPrimaryCaptureHoldsAuxiliaryInputs),
    ("ASIO input capture converts interleaved floats", AsioInputCaptureConvertsInterleavedFloats),
    ("CoreAudio session catalog skips ASIO outputs", CoreAudioSessionCatalogSkipsAsioOutputs),
    ("Processed output status reports actual playback format", ProcessedOutputStatusReportsActualPlaybackFormat),
    ("Input channel modes map interface lanes", InputChannelModesMapInterfaceLanes),
    ("Input channel mode falls back for mono devices", InputChannelModeFallsBackForMonoDevices),
    ("Audio device refresh suppresses mic selection churn", AudioDeviceRefreshSuppressesMicSelectionChurn),
    ("Mic DSP tab excludes loopback inputs", MicDspTabExcludesLoopbackInputs),
    ("System audio loopback is selectable but not default mic fallback", SystemAudioLoopbackIsSelectableButNotDefaultMicFallback),
    ("App audio loopback routes through the mixer capture path", AppAudioLoopbackRoutesThroughMixerCapturePath),
    ("Loopback captures shut down without zombie workers", LoopbackCapturesShutDownWithoutZombieWorkers),
    ("System audio loopback mixer strip is left of mics", SystemAudioLoopbackMixerStripIsLeftOfMics),
    ("Stereo input DSP applies independently per channel", StereoInputDspAppliesIndependentlyPerChannel),
    ("System audio loopback stereo provider preserves channels", SystemAudioLoopbackStereoProviderPreservesChannels),
    ("NAudio stereo test tone routes through mixer inputs", NAudioStereoTestToneRoutesThroughMixerInputs),
    ("Blank mixer channels restore without fallback input", BlankMixerChannelsRestoreWithoutFallbackInput),
    ("App settings roundtrip preserves mic mixer routing state", AppSettingsRoundtripPreservesMicMixerRoutingState),
    ("Primary capture selector follows active mic source", PrimaryCaptureSelectorFollowsActiveMicSource),
    ("Primary capture selector matches ASIO endpoint", PrimaryCaptureSelectorMatchesAsioEndpoint),
    ("Audio recording filenames identify selected source", AudioRecordingFilenamesIdentifySelectedSource),
    ("Audio recording wave format follows selected source", AudioRecordingWaveFormatFollowsSelectedSource),
    ("Live service bus records interface left and right", LiveServiceBusRecordsInterfaceLeftAndRight),
    ("Live service bus records higher interface lanes", LiveServiceBusRecordsHigherInterfaceLanes),
    ("Live service bus records selected mic sources", LiveServiceBusRecordsSelectedMicSources),
    ("Live output provider receives program mix", LiveOutputProviderReceivesProgramMix),
    ("Live output provider follows mixer mute and solo", LiveOutputProviderFollowsMixerMuteAndSolo),
    ("Live output provider follows mixer gain pan polarity and delay", LiveOutputProviderFollowsMixerGainPanPolarityAndDelay),
    ("Live service bus mixes auxiliary capture device", LiveServiceBusMixesAuxiliaryCaptureDevice),
    ("Live service bus publishes auxiliary sync telemetry", LiveServiceBusPublishesAuxiliarySyncTelemetry),
    ("Live service bus records auxiliary mic in program mix", LiveServiceBusRecordsAuxiliaryMicInProgramMix),
    ("Live service bus records selected auxiliary mic sources", LiveServiceBusRecordsSelectedAuxiliaryMicSources),
    ("Processed audio converter writes output formats", ProcessedAudioConverterWritesOutputFormats),
    ("Processed audio converter rechannels recording sources", ProcessedAudioConverterRechannelsRecordingSources),
    ("Spectrum frame router maps selected mics and program output", SpectrumFrameRouterMapsSelectedMicsAndProgramOutput),
    ("DX12 graph modes match selected mic and mixer roles", Dx12GraphModesMatchSelectedMicAndMixerRoles),
    ("Spectrum analyzer emits high-resolution bins", SpectrumAnalyzerEmitsHighResolutionBins),
    ("Feedback detector catches narrow runaway spikes", FeedbackDetectorCatchesNarrowRunawaySpikes),
    ("Mix bus processor scales and protects output", MixBusProcessorScalesAndProtectsOutput),
    ("NAudio program bus mixes one-shot mic blocks", NAudioProgramBusMixesOneShotMicBlocks),
    ("NAudio peak meter reports provider peaks", NAudioPeakMeterReportsProviderPeaks),
    ("Spectrum lines carry NAudio metered peaks", SpectrumLinesCarryNaudioMeteredPeaks),
    ("Mixer strip meters ignore muted raw input", MixerStripMetersIgnoreMutedRawInput),
    ("Live program mix bus combines ten mic feeds", LiveProgramMixBusCombinesTenMicFeeds),
    ("Live mix audibility gates mute and solo", LiveMixAudibilityGatesMuteAndSolo),
    ("Mixer strip clicks select channels cheaply", MixerStripClicksSelectChannelsCheaply),
    ("Active mic selection avoids synchronous format probe", ActiveMicSelectionAvoidsSynchronousFormatProbe),
    ("Mixer channel controls debounce state persistence", MixerChannelControlsDebounceStatePersistence),
    ("Mixer volume controls mark and snap unity", MixerVolumeControlsMarkAndSnapUnity),
    ("Stereo pan provider routes mono mics across stereo bus", StereoPanProviderRoutesMonoMicsAcrossStereoBus),
    ("Audio delay line delays and resets samples", AudioDelayLineDelaysAndResetsSamples),
    ("Stereo audio delay line preserves left and right", StereoAudioDelayLinePreservesLeftAndRight),
    ("Audio sync buffer holds target latency", AudioSyncBufferHoldsTargetLatency),
    ("Audio sync buffer preserves interleaved stereo", AudioSyncBufferPreservesInterleavedStereo),
    ("Audio sync buffer resamples and trims drift", AudioSyncBufferResamplesAndTrimsDrift),
    ("Audio sync buffer uses NAudio WDL resampler", AudioSyncBufferUsesNAudioWdlResampler),
    ("Processed output resamples ASIO fallback formats", ProcessedOutputResamplesAsioFallbackFormats),
    ("Camera mode auto display text is stable", CameraModeAutoDisplayText),
    ("Enhanced karaoke LRC parses inline timings", EnhancedKaraokeLrcParsesInlineTimings),
    ("Timed karaoke word selection waits for first timestamp", TimedKaraokeWordSelectionWaitsForFirstTimestamp),
    ("Short karaoke words stay visible across close timestamps", ShortKaraokeWordsStayVisibleAcrossCloseTimestamps),
    ("Karaoke lyric cache is scoped by track file", KaraokeLyricCacheIsScopedByTrackFile),
    ("Karaoke M4A duration reads MP4 movie header", KaraokeM4aDurationReadsMovieHeader),
    ("Karaoke sample reader accepts extended formats", KaraokeSampleReaderAcceptsExtendedFormats),
    ("Karaoke sample reader failures use media fallback", KaraokeSampleReaderFailuresUseMediaFallback),
    ("Karaoke playback-stopped codec failures use media fallback", KaraokePlaybackStoppedCodecFailuresUseMediaFallback),
    ("Karaoke play restarts after track end", KaraokePlayRestartsAfterTrackEnd),
    ("Karaoke add tracks loads idle selection", KaraokeAddTracksLoadsIdleSelection),
    ("Audio recording browser accepts extended playback formats", AudioRecordingBrowserAcceptsExtendedPlaybackFormats),
    ("Audio recording exporter supports compressed targets", AudioRecordingExporterSupportsCompressedTargets),
    ("Karaoke browser DFS hides M4P tracks", KaraokeBrowserDfsHidesM4pTracks),
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

static void AppStorageUsesLocalAppDataAndRotatesDiagnostics()
{
    var storageSource = File.ReadAllText(FindRepoFile("AppStoragePaths.cs"));
    Assert(storageSource.Contains("Environment.SpecialFolder.LocalApplicationData", StringComparison.Ordinal), "app storage should use the per-user LocalAppData folder");
    Assert(storageSource.Contains("AppDataFolderName = \"JerichoDown\"", StringComparison.Ordinal), "app storage should be rooted in a JerichoDown folder");
    Assert(storageSource.Contains("LegacySettingsFolder", StringComparison.Ordinal), "app storage should retain a legacy settings migration path");
    Assert(storageSource.Contains("CopyLegacyDirectory", StringComparison.Ordinal), "legacy settings should be migrated into LocalAppData without user data loss");
    Assert(storageSource.Contains("run-state.json", StringComparison.Ordinal) && storageSource.Contains("diagnostics.log", StringComparison.Ordinal), "legacy volatile run state and logs should not be copied as persistent state");

    var stateSource = File.ReadAllText(FindRepoFile("AppStateStore.cs"));
    Assert(stateSource.Contains("MaximumDiagnosticsLogBytes", StringComparison.Ordinal), "diagnostics logging should have a size cap");
    Assert(stateSource.Contains("RetainedDiagnosticsLogCount", StringComparison.Ordinal), "diagnostics logging should retain a bounded number of rotated logs");
    Assert(stateSource.Contains("RotateDiagnosticsLogIfNeeded", StringComparison.Ordinal), "diagnostics logging should rotate before appending forever");
    Assert(stateSource.Contains("DiagnosticsLogLock", StringComparison.Ordinal), "diagnostics logging should serialize rotation and append operations");
}

static void AppGeneratedFilesUseAtomicWrites()
{
    var atomicSource = File.ReadAllText(FindRepoFile("AtomicFile.cs"));
    Assert(atomicSource.Contains("File.Replace", StringComparison.Ordinal), "atomic writes should replace existing files through the filesystem replace primitive");
    Assert(atomicSource.Contains("Guid.NewGuid", StringComparison.Ordinal), "atomic writes should use unique temp files");

    var stateSource = File.ReadAllText(FindRepoFile("AppStateStore.cs"));
    Assert(!stateSource.Contains("            File.WriteAllText(SettingsPath", StringComparison.Ordinal), "settings state should not be written directly");
    Assert(!stateSource.Contains("            File.WriteAllText(RunMarkerPath", StringComparison.Ordinal), "run state should not be written directly");
    Assert(stateSource.Contains("AtomicFile.WriteAllText(SettingsPath", StringComparison.Ordinal), "settings state should use atomic writes");
    Assert(stateSource.Contains("AtomicFile.WriteAllText(RunMarkerPath", StringComparison.Ordinal), "run state should use atomic writes");

    var cameraSource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraProfileStore.cs")));
    Assert(cameraSource.Contains("AtomicFile.WriteAllText", StringComparison.Ordinal), "camera profiles should use atomic writes");

    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowSource.Contains("AtomicFile.WriteAllText(metadataPath", StringComparison.Ordinal), "podcast session metadata should use atomic writes");
    Assert(windowSource.Contains("AtomicFile.WriteAllText(cachePath", StringComparison.Ordinal), "karaoke lyric cache should use atomic writes");
    Assert(windowSource.Contains("AtomicFile.WriteAllText(GetUserPresetPath", StringComparison.Ordinal), "user presets should use atomic writes");
}

static void RecordingDeletesArePathBounded()
{
    var pathSafetySource = File.ReadAllText(FindRepoFile("PathSafety.cs"));
    Assert(pathSafetySource.Contains("IsRegularFileUnderFolder", StringComparison.Ordinal), "path safety should validate files against configured roots");
    Assert(pathSafetySource.Contains("IsDirectoryUnderFolder", StringComparison.Ordinal), "path safety should validate folders against configured roots");
    Assert(pathSafetySource.Contains("FileAttributes.ReparsePoint", StringComparison.Ordinal), "path safety should reject reparse points before destructive operations");
    Assert(pathSafetySource.Contains("ArgumentList.Add", StringComparison.Ordinal), "explorer launch should use structured argument passing");

    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var audioDelete = ExtractSourceBetween(windowSource, "    private void DeleteSelectedAudioRecording()", "    private void SessionFilesSelectionChanged");
    Assert(audioDelete.Contains("PathSafety.IsRegularFileUnderFolder(selectedPath, _audioRecordingFolder", StringComparison.Ordinal), "audio recording deletes should be bounded to the recording folder");
    Assert(audioDelete.Contains("Delete blocked", StringComparison.Ordinal), "audio recording deletes should fail closed when path checks fail");

    var sessionDelete = ExtractSourceBetween(windowSource, "    private void DeleteSelectedSessionRecording()", "    private void StartRecordingClicked");
    Assert(sessionDelete.Contains("PathSafety.IsDirectoryUnderFolder(selected.SessionFolder, _outputFolder", StringComparison.Ordinal), "session folder deletes should be bounded to the output folder");
    Assert(sessionDelete.Contains("PathSafety.IsRegularFileUnderFolder(selected.Path, selected.SessionFolder, \".mp4\")", StringComparison.Ordinal), "session deletes should verify the selected video is inside the selected session folder");

    var karaokeDelete = ExtractSourceBetween(windowSource, "    private void DeleteSelectedKaraokeRecording()", "    private void StartKaraokeRecordingPlayback");
    Assert(karaokeDelete.Contains("PathSafety.IsRegularFileUnderFolder(selectedPath, _karaokeRecordingFolder", StringComparison.Ordinal), "karaoke recording deletes should be bounded to the karaoke recording folder");

    Assert(windowSource.Contains("PathSafety.RevealFileInExplorer", StringComparison.Ordinal), "open-location handlers should use the path-safe explorer helper");
    Assert(windowSource.Contains("Open location blocked", StringComparison.Ordinal), "open-location handlers should fail closed when selected paths are outside configured roots");
}

static void KaraokeAiToolsUseStructuredProcessArguments()
{
    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowSource.Contains("CreateKaraokeAiWorkFolder", StringComparison.Ordinal), "AI lyric detection should create isolated work folders per run");
    Assert(windowSource.Contains("detect_{DateTime.UtcNow", StringComparison.Ordinal) && windowSource.Contains("Guid.NewGuid", StringComparison.Ordinal), "AI lyric detection work folders should be unique");
    Assert(windowSource.Contains("PathSafety.IsDirectoryUnderFolder(workFolder, KaraokeAiWorkFolder, allowRoot: false)", StringComparison.Ordinal), "AI work-folder cleanup should stay under the app work root");
    Assert(windowSource.Contains("IReadOnlyList<string> arguments", StringComparison.Ordinal), "external karaoke tools should pass structured argument lists");
    Assert(windowSource.Contains("process.StartInfo.ArgumentList.Add(argument)", StringComparison.Ordinal), "external karaoke tools should use ProcessStartInfo.ArgumentList");
    Assert(!windowSource.Contains("QuoteProcessArgument", StringComparison.Ordinal), "manual process argument quoting should not be used");
    Assert(!windowSource.Contains("Arguments = arguments", StringComparison.Ordinal), "external karaoke tools should not build a single command-line argument string");
    Assert(windowSource.Contains("Kill(entireProcessTree: true)", StringComparison.Ordinal), "timed-out external karaoke tools should clean up child processes");
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

static void VoiceShelfEqShapesLowBodyAndHighAir()
{
    const int sampleRate = 48_000;
    var lowTone = GenerateSine(sampleRate, 120d, 0.35d, 2.0d);
    var midTone = GenerateSine(sampleRate, 1_000d, 0.35d, 2.0d);
    var highTone = GenerateSine(sampleRate, 10_000d, 0.35d, 2.0d);
    var lowBypass = ProcessShelfEqTestTone(lowTone, enabled: false);
    var lowShaped = ProcessShelfEqTestTone(lowTone, enabled: true);
    var midBypass = ProcessShelfEqTestTone(midTone, enabled: false);
    var midShaped = ProcessShelfEqTestTone(midTone, enabled: true);
    var highBypass = ProcessShelfEqTestTone(highTone, enabled: false);
    var highShaped = ProcessShelfEqTestTone(highTone, enabled: true);

    var start = sampleRate;
    var lowBypassRms = CalculateTailRms(lowBypass, start);
    var lowShapedRms = CalculateTailRms(lowShaped, start);
    var midBypassRms = CalculateTailRms(midBypass, start);
    var midShapedRms = CalculateTailRms(midShaped, start);
    var highBypassRms = CalculateTailRms(highBypass, start);
    var highShapedRms = CalculateTailRms(highShaped, start);

    Assert(lowShapedRms > lowBypassRms * 1.45d, "low shelf boost should raise low body");
    Assert(highShapedRms < highBypassRms * 0.70d, "high shelf cut should tame high air");
    Assert(midShapedRms > midBypassRms * 0.82d && midShapedRms < midBypassRms * 1.22d, "shelf EQ should keep mid voice mostly stable");
    Assert(lowShaped.All(float.IsFinite) && highShaped.All(float.IsFinite), "shelf EQ output should stay finite");
}

static void DspVerificationReportProvesCustomDspClaims()
{
    var report = DspVerificationReportGenerator.Run();
    var failedChecks = report.Checks
        .Where(check => !check.Passed)
        .Select(check => $"{check.Effect}: {check.Claim} measured {check.Measurement}, required {check.Requirement} ({check.Details})");

    Assert(report.Passed, "DSP verification report should pass all custom EQ/DSP checks: " + string.Join("; ", failedChecks));
    Assert(report.Checks.Count >= 40, "DSP verification report should include the practical customer-facing custom DSP proof checks");
    foreach (var effect in new[]
    {
        "Graphic EQ",
        "Input trim",
        "Makeup gain",
        "High-pass filter",
        "Low-pass filter",
        "Hum removal",
        "Notch filter",
        "Parametric EQ",
        "Shelf EQ",
        "De-popper",
        "Noise gate",
        "Expander",
        "Noise suppression",
        "Echo reducer",
        "Compressor",
        "Breath reducer",
        "De-esser",
        "Presence enhancer",
        "Saturation",
        "Limiter",
        "Full custom DSP chain"
    })
    {
        Assert(report.Checks.Any(check => check.Effect == effect), $"DSP verification report should include {effect}");
    }

    var markdown = DspVerificationReportGenerator.CreateMarkdownReport(report);
    Assert(markdown.Contains("Jericho Down DSP Verification", StringComparison.Ordinal), "verification report should have a clear title");
    Assert(markdown.Contains("known tone, noise, transient, and composite test signals", StringComparison.Ordinal), "verification report should explain the test method");
    Assert(markdown.Contains("NAudio-branded effects are intentionally outside", StringComparison.Ordinal), "verification report should explain why NAudio effects are out of scope");
    Assert(markdown.Contains("| Effect | Claim | Measurement | Requirement | Result | Details |", StringComparison.Ordinal), "verification report should include a customer-readable table");
}

static void NAudioBiQuadRackExposesEveryEqShape()
{
    const int sampleRate = 48_000;
    var source = GenerateSine(sampleRate, 1_000, 0.16, 0.45);
    var start = sampleRate / 3;
    var bypass = ProcessNAudioBiQuadTestTone(source, _ => { });
    var peaking = ProcessNAudioBiQuadTestTone(source, settings =>
    {
        settings.NAudioPeakingEqEnabled = true;
        settings.NAudioPeakingEqFrequencyHz = 1_000;
        settings.NAudioPeakingEqGainDb = 9;
        settings.NAudioPeakingEqQ = 2.4;
    });
    var notch = ProcessNAudioBiQuadTestTone(source, settings =>
    {
        settings.NAudioNotchEnabled = true;
        settings.NAudioNotchFrequencyHz = 1_000;
        settings.NAudioNotchQ = 12;
    });

    var bypassRms = CalculateTailRms(bypass, start);
    Assert(CalculateTailRms(peaking, start) > bypassRms * 1.75d, "NAudio peaking EQ should boost the selected frequency");
    Assert(CalculateTailRms(notch, start) < bypassRms * 0.35d, "NAudio notch should cut the selected frequency");
    Assert(peaking.All(float.IsFinite) && notch.All(float.IsFinite), "NAudio BiQuad output should stay finite");

    var rackSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioBiQuadFilterRack.cs")));
    foreach (var factory in new[]
    {
        "LowPassFilter",
        "HighPassFilter",
        "BandPassFilterConstantPeakGain",
        "BandPassFilterConstantSkirtGain",
        "NotchFilter",
        "AllPassFilter",
        "PeakingEQ",
        "LowShelf",
        "HighShelf"
    })
    {
        Assert(rackSource.Contains($"BiQuadFilter.{factory}", StringComparison.Ordinal), $"NAudio BiQuad rack should expose {factory}");
    }

    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(xaml.Contains("NAudio BiQuad Filter Rack", StringComparison.Ordinal), "BiQuad controls should be labeled as one NAudio family");
    Assert(xaml.Contains("BiQuad family", StringComparison.Ordinal), "BiQuad controls should show the shared BiQuad family frame label");
    var biQuadGroup = ExtractSourceBetween(
        xaml,
        "Text=\"NAudio BiQuad Filter Rack\"",
        "Text=\"NAudio Special Effects\"");
    foreach (var header in new[]
    {
        "BiQuad Low-pass",
        "BiQuad High-pass",
        "BiQuad Band-pass peak",
        "BiQuad Band-pass skirt",
        "BiQuad Notch",
        "BiQuad All-pass",
        "BiQuad Peaking EQ",
        "BiQuad Low shelf",
        "BiQuad High shelf"
    })
    {
        Assert(biQuadGroup.Contains($"Expander Header=\"{header}\"", StringComparison.Ordinal), $"BiQuad group should collapse {header} controls under the shared rack");
    }
}

static void NAudioPitchShiftMovesToneFrequency()
{
    const int sampleRate = 48_000;
    var source = GenerateSine(sampleRate, 440, 0.35, 1.0);
    var shifted = ProcessNAudioPitchShiftTestTone(source, settings =>
    {
        settings.NAudioPitchShiftEnabled = true;
        settings.NAudioPitchShiftSemitones = 12;
        settings.NAudioPitchShiftFftSize = 1024;
        settings.NAudioPitchShiftOversampling = 8;
        settings.NAudioPitchShiftMix = 1;
    });

    var start = sampleRate / 2;
    var sourceMagnitude = CalculateToneMagnitude(shifted, sampleRate, 440, start, 8192);
    var shiftedMagnitude = CalculateToneMagnitude(shifted, sampleRate, 880, start, 8192);

    Assert(shiftedMagnitude > sourceMagnitude * 1.25d, "NAudio SmbPitchShifter should move a +12 semitone tone toward the octave");
    Assert(shifted.All(float.IsFinite), "NAudio pitch shift output should stay finite");

    var processor = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioPitchShiftProcessor.cs")));
    Assert(processor.Contains("SmbPitchShifter", StringComparison.Ordinal), "NAudio pitch shift should use SmbPitchShifter");

    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(xaml.Contains("NAudio Special Effects", StringComparison.Ordinal), "NAudio special effects controls should be labeled as one family");
    var specialEffectsGroup = ExtractSourceBetween(
        xaml,
        "Text=\"NAudio Special Effects\"",
        "Text=\"NAudio DMO Effects\"");
    foreach (var header in new[]
    {
        "NAudio Pitch Shift",
        "NAudio Convolution",
        "NAudio EnvelopeGenerator"
    })
    {
        Assert(specialEffectsGroup.Contains($"Expander Header=\"{header}\"", StringComparison.Ordinal), $"{header} controls should collapse under the NAudio Special Effects family");
    }
}

static void NAudioConvolutionAddsGeneratedImpulseTail()
{
    const int sampleRate = 48_000;
    var impulse = new float[sampleRate / 2];
    impulse[0] = 0.65f;

    var bypass = ProcessNAudioConvolutionTestTone(impulse, _ => { });
    var convolved = ProcessNAudioConvolutionTestTone(impulse, settings =>
    {
        settings.NAudioConvolutionEnabled = true;
        settings.NAudioConvolutionLengthMs = 90;
        settings.NAudioConvolutionPreDelayMs = 6;
        settings.NAudioConvolutionDecay = 0.8;
        settings.NAudioConvolutionMix = 1;
    });

    var tailStart = sampleRate / 100;
    Assert(CalculateTailRms(convolved, tailStart) > CalculateTailRms(bypass, tailStart) * 3d + 0.0005d, "NAudio convolution should add an audible generated impulse tail");
    Assert(convolved.All(float.IsFinite), "NAudio convolution output should stay finite");

    var processor = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioImpulseConvolutionProcessor.cs")));
    Assert(processor.Contains("ImpulseResponseConvolution", StringComparison.Ordinal), "NAudio convolution should use ImpulseResponseConvolution");
    Assert(processor.Contains(".Convolve(", StringComparison.Ordinal), "NAudio convolution should call Convolve");
    Assert(processor.Contains(".Normalize(", StringComparison.Ordinal), "NAudio convolution should normalize generated impulses");

    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var specialEffectsGroup = ExtractSourceBetween(
        xaml,
        "Text=\"NAudio Special Effects\"",
        "Text=\"NAudio DMO Effects\"");
    Assert(specialEffectsGroup.Contains("Expander Header=\"NAudio Convolution\"", StringComparison.Ordinal), "NAudio convolution controls should be grouped under NAudio Special Effects");
}

static void NAudioEnvelopeGeneratorShapesAttack()
{
    const int sampleRate = 48_000;
    var source = GenerateSine(sampleRate, 440, 0.45, 1.0);
    var shaped = ProcessNAudioEnvelopeTestTone(source, settings =>
    {
        settings.NAudioEnvelopeEnabled = true;
        settings.NAudioEnvelopeTriggerThresholdDb = -60;
        settings.NAudioEnvelopeAttackMs = 250;
        settings.NAudioEnvelopeDecayMs = 1;
        settings.NAudioEnvelopeSustainLevel = 1;
        settings.NAudioEnvelopeReleaseMs = 120;
        settings.NAudioEnvelopeMix = 1;
    });

    var earlyRms = CalculateWindowRms(shaped, sampleRate / 100, sampleRate / 50);
    var sustainedRms = CalculateWindowRms(shaped, sampleRate / 2, sampleRate / 10);
    Assert(earlyRms < sustainedRms * 0.45d, "NAudio EnvelopeGenerator should ramp in with a long attack");
    Assert(shaped.All(float.IsFinite), "NAudio envelope output should stay finite");

    var processor = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioEnvelopeGeneratorProcessor.cs")));
    Assert(processor.Contains("EnvelopeGenerator", StringComparison.Ordinal), "NAudio envelope should use EnvelopeGenerator");

    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var specialEffectsGroup = ExtractSourceBetween(
        xaml,
        "Text=\"NAudio Special Effects\"",
        "Text=\"NAudio DMO Effects\"");
    Assert(specialEffectsGroup.Contains("Expander Header=\"NAudio EnvelopeGenerator\"", StringComparison.Ordinal), "NAudio envelope controls should be grouped under NAudio Special Effects");
}

static void NAudioDmoEffectChainExposesDirectSoundEffects()
{
    var processor = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioDmoEffectChain.cs")));
    foreach (var effectType in new[]
    {
        "DmoChorus",
        "DmoFlanger",
        "DmoEcho",
        "DmoDistortion",
        "DmoCompressor",
        "DmoParamEq",
        "DmoGargle",
        "DmoI3DL2Reverb",
        "DmoWavesReverb"
    })
    {
        Assert(processor.Contains(effectType, StringComparison.Ordinal), $"NAudio DMO chain should expose {effectType}");
    }

    Assert(processor.Contains("MediaObjectInPlace.Process", StringComparison.Ordinal), "NAudio DMO chain should process blocks through MediaObjectInPlace");
    Assert(processor.Contains("SupportsInputWaveFormat", StringComparison.Ordinal), "NAudio DMO chain should check DMO format compatibility");

    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(xaml.Contains("NAudio DMO Effects", StringComparison.Ordinal), "NAudio DMO controls should be labeled as one family");
    Assert(xaml.Contains("DMO I3DL2 Reverb", StringComparison.Ordinal), "NAudio DMO controls should include I3DL2 reverb");
    Assert(xaml.Contains("DMO Waves Reverb", StringComparison.Ordinal), "NAudio DMO controls should include Waves reverb");
}

static void DspScreenSeparatesCustomAndNaudioFamilies()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));

    Assert(xaml.Contains("Text=\"Jericho DSP\"", StringComparison.Ordinal), "custom DSP controls should have a Jericho DSP family bubble");
    Assert(xaml.Contains("Text=\"Custom chain\"", StringComparison.Ordinal), "custom DSP bubble should be labeled as the custom chain");
    Assert(xaml.Contains("Text=\"NAudio DSP\"", StringComparison.Ordinal), "NAudio controls should have a NAudio DSP family bubble");
    Assert(xaml.Contains("Text=\"Library-backed\"", StringComparison.Ordinal), "NAudio DSP bubble should be labeled as library-backed");
    Assert(xaml.Contains("x:Key=\"DspGroupedPanel\"", StringComparison.Ordinal), "family bubbles should use flat nested DSP panels");

    var customGroup = ExtractSourceBetween(xaml, "Text=\"Jericho DSP\"", "Text=\"NAudio DSP\"");
    foreach (var marker in new[]
    {
        "Text=\"Input Trim\"",
        "Content=\"De-popper\"",
        "Content=\"High-pass filter\"",
        "Content=\"Low-pass filter\"",
        "Content=\"Hum removal\"",
        "Content=\"Notch filter\"",
        "Content=\"Parametric EQ\"",
        "Content=\"Shelf EQ\"",
        "Content=\"Noise suppression\"",
        "Content=\"Expander\"",
        "Content=\"Noise gate\"",
        "Content=\"Echo reducer\"",
        "Content=\"Compressor\"",
        "Content=\"De-esser\"",
        "Content=\"Breath reducer\"",
        "Content=\"Presence enhancer\"",
        "Content=\"Warmth\"",
        "Text=\"Makeup Gain\"",
        "Content=\"Limiter\""
    })
    {
        Assert(customGroup.Contains(marker, StringComparison.Ordinal), $"Jericho DSP group should contain {marker}");
    }

    Assert(!customGroup.Contains("NAudio BiQuad Filter Rack", StringComparison.Ordinal), "Jericho DSP group should not contain NAudio controls");
    Assert(Regex.Matches(customGroup, "StaticResource DspGroupedPanel").Count >= 10, "custom DSP controls should be nested inside the Jericho DSP bubble");

    var naudioGroup = ExtractSourceBetween(xaml, "Text=\"NAudio DSP\"", "<TabItem x:Name=\"MixingTabItem\" Header=\"Mixing\">");
    foreach (var marker in new[]
    {
        "Text=\"NAudio BiQuad Filter Rack\"",
        "Text=\"NAudio Special Effects\"",
        "Text=\"NAudio DMO Effects\""
    })
    {
        Assert(naudioGroup.Contains(marker, StringComparison.Ordinal), $"NAudio DSP group should contain {marker}");
    }

    Assert(!naudioGroup.Contains("Text=\"Input Trim\"", StringComparison.Ordinal), "NAudio DSP group should not contain custom controls");
    Assert(!naudioGroup.Contains("Content=\"Noise gate\"", StringComparison.Ordinal), "NAudio DSP group should not contain custom gate controls");
    Assert(!naudioGroup.Contains("StaticResource DspBubble", StringComparison.Ordinal), "individual NAudio sections should be flat grouped panels inside the NAudio DSP bubble");
    Assert(Regex.Matches(naudioGroup, "StaticResource DspGroupedPanel").Count >= 3, "NAudio DSP sections should be nested inside the NAudio DSP bubble");
}

static void NAudioDmoEffectChainProcessesSafely()
{
    var source = GenerateSine(48_000, 440, 0.22, 0.12);
    var output = ProcessNAudioDmoTestTone(source, settings =>
    {
        settings.NAudioDmoChorusEnabled = true;
        settings.NAudioDmoFlangerEnabled = true;
        settings.NAudioDmoEchoEnabled = true;
        settings.NAudioDmoDistortionEnabled = true;
        settings.NAudioDmoCompressorEnabled = true;
        settings.NAudioDmoParamEqEnabled = true;
        settings.NAudioDmoGargleEnabled = true;
        settings.NAudioDmoI3DL2ReverbEnabled = true;
        settings.NAudioDmoWavesReverbEnabled = true;
        settings.NAudioDmoEchoWetDryMix = 10;
        settings.NAudioDmoEchoFeedback = 5;
        settings.NAudioDmoDistortionEdge = 5;
        settings.NAudioDmoCompressorRatio = 2;
        settings.NAudioDmoParamEqGainDb = 3;
        settings.NAudioDmoWavesReverbMixDb = -18;
    });

    Assert(output.Length == source.Length, "NAudio DMO processing should preserve sample count");
    Assert(output.All(float.IsFinite), "NAudio DMO processing should keep output finite even when optional Windows DMOs are unavailable");
}

static void NAudioMidiSupportExposesInputOutputAndFileFeatures()
{
    var catalog = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiDeviceCatalog.cs")));
    Assert(catalog.Contains("MidiIn.NumberOfDevices", StringComparison.Ordinal), "MIDI catalog should enumerate NAudio input devices");
    Assert(catalog.Contains("MidiOut.NumberOfDevices", StringComparison.Ordinal), "MIDI catalog should enumerate NAudio output devices");

    var monitor = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiInputMonitor.cs")));
    Assert(monitor.Contains("MessageReceived", StringComparison.Ordinal), "MIDI input monitor should receive short messages");
    Assert(monitor.Contains("ErrorReceived", StringComparison.Ordinal), "MIDI input monitor should surface input errors");
    Assert(monitor.Contains("SysexMessageReceived", StringComparison.Ordinal), "MIDI input monitor should receive sysex messages");
    Assert(monitor.Contains("CreateSysexBuffers", StringComparison.Ordinal), "MIDI input monitor should allocate sysex buffers");

    var output = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiOutputPort.cs")));
    foreach (var api in new[] { "StartNote", "StopNote", "ChangeControl", "ChangePatch", "SendBankSelect", "SendAllNotesOff", "SendResetAllControllers", "SendBuffer", "Reset" })
    {
        Assert(output.Contains(api, StringComparison.Ordinal), $"MIDI output should expose NAudio {api}");
    }

    var fileService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiFileService.cs")));
    Assert(fileService.Contains("new MidiFile", StringComparison.Ordinal), "MIDI file service should read NAudio MIDI files");
    Assert(fileService.Contains("MidiFile.Export", StringComparison.Ordinal), "MIDI file service should export NAudio MIDI files");
    Assert(fileService.Contains("MidiTrackSummary", StringComparison.Ordinal), "MIDI file service should expose file-test track summaries");

    var sequenceService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiSequenceService.cs")));
    Assert(sequenceService.Contains("GetAsShortMessage", StringComparison.Ordinal), "MIDI sequence service should emit playable short messages");
    Assert(sequenceService.Contains("TempoEvent", StringComparison.Ordinal), "MIDI sequence service should respect MIDI tempo events");

    var soundFontLibrary = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "SoundFontLibrary.cs")));
    Assert(soundFontLibrary.Contains("new SoundFont", StringComparison.Ordinal), "SoundFont library should read NAudio SoundFont files");
    Assert(soundFontLibrary.Contains("Presets", StringComparison.Ordinal), "SoundFont library should expose presets");
    Assert(soundFontLibrary.Contains("Instruments", StringComparison.Ordinal), "SoundFont library should expose instruments");
    Assert(soundFontLibrary.Contains("SampleHeaders", StringComparison.Ordinal), "SoundFont library should expose samples");
    Assert(soundFontLibrary.Contains("CreateSamplePreviewStream", StringComparison.Ordinal), "SoundFont library should create sample preview streams");
    Assert(soundFontLibrary.Contains("RawSourceWaveStream", StringComparison.Ordinal), "SoundFont sample preview should use NAudio wave playback primitives");

    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(xaml.Contains("Header=\"MIDI\"", StringComparison.Ordinal), "Main tabs should expose a MIDI tab");
    Assert(xaml.Contains("MIDI Utility", StringComparison.Ordinal), "MIDI tab should present itself as a utility surface");
    Assert(xaml.Contains("MidiInputDeviceComboBox", StringComparison.Ordinal), "MIDI tab should expose input device selection");
    Assert(xaml.Contains("MidiOutputDeviceComboBox", StringComparison.Ordinal), "MIDI tab should expose output device selection");
    Assert(xaml.Contains("SelectionChanged=\"MidiDeviceSelectionChanged\"", StringComparison.Ordinal), "MIDI device selections should persist through a shared handler");
    Assert(xaml.Contains("MidiInputDeviceDetailsText", StringComparison.Ordinal), "MIDI tab should show selected input diagnostics");
    Assert(xaml.Contains("MidiOutputDeviceDetailsText", StringComparison.Ordinal), "MIDI tab should show selected output diagnostics");
    Assert(xaml.Contains("MidiSelectedMessageDetailsText", StringComparison.Ordinal), "MIDI tab should inspect the selected monitor message");
    Assert(xaml.Contains("SendMidiSysexClicked", StringComparison.Ordinal), "MIDI tab should expose sysex output");
    Assert(xaml.Contains("MidiControlMappingActionComboBox", StringComparison.Ordinal), "MIDI tab should expose control mapping actions");
    Assert(xaml.Contains("MidiSequenceTracksItemsControl", StringComparison.Ordinal), "MIDI file test should expose track summaries");
    Assert(xaml.Contains("PlayMidiSequenceClicked", StringComparison.Ordinal), "MIDI tab should play MIDI files to the selected output");
    Assert(xaml.Contains("StopMidiSequenceClicked", StringComparison.Ordinal), "MIDI tab should stop MIDI file tests");
    Assert(xaml.Contains("MidiSequenceTempoSlider", StringComparison.Ordinal), "MIDI tab should control MIDI file-test speed");
    Assert(xaml.Contains("MidiSoundFontPresetComboBox", StringComparison.Ordinal), "MIDI tab should expose SoundFont presets");
    Assert(xaml.Contains("LoadSoundFontClicked", StringComparison.Ordinal), "MIDI tab should load SoundFont files");
    Assert(xaml.Contains("Send Bank + Patch", StringComparison.Ordinal), "MIDI tab should send SoundFont bank and patch selections to the selected output");
    Assert(xaml.Contains("PreviewSoundFontNoteClicked", StringComparison.Ordinal), "MIDI tab should send selected SoundFont preset preview notes through MIDI output");
    Assert(xaml.Contains("PreviewSoundFontSampleClicked", StringComparison.Ordinal), "MIDI tab should preview loaded SoundFont samples inside the app");
    Assert(xaml.Contains("MidiSoundFontSampleSelectionChanged", StringComparison.Ordinal), "MIDI tab should update sample preview state when sample selection changes");
    foreach (var outputControl in new[]
             {
                 "MidiNoteOnButton",
                 "MidiNoteOffButton",
                 "MidiControlChangeButton",
                 "MidiPatchChangeButton",
                 "MidiPitchWheelButton",
                 "MidiRawSendButton",
                 "MidiSysexSendButton",
                 "MidiAllNotesOffButton",
                 "MidiResetControllersButton",
                 "MidiSoundFontApplyButton",
                 "MidiSoundFontPreviewButton"
             })
    {
        Assert(xaml.Contains(outputControl, StringComparison.Ordinal), $"MIDI workflow should name {outputControl} for release-state enablement");
    }

    Assert(xaml.Contains("Refresh MIDI Devices", StringComparison.Ordinal), "File menu should expose MIDI device refresh");

    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowSource.Contains("RestoreMidiWorkflowState", StringComparison.Ordinal), "MIDI workflow should restore persisted state");
    Assert(windowSource.Contains("SelectMidiInputDevice", StringComparison.Ordinal), "MIDI input selection should restore by saved device identity");
    Assert(windowSource.Contains("SelectMidiOutputDevice", StringComparison.Ordinal), "MIDI output selection should restore by saved device identity");
    Assert(windowSource.Contains("MidiSequenceSpeedPercent", StringComparison.Ordinal), "MIDI file-test speed should be captured in app state");
    Assert(windowSource.Contains("StopMidiSequencePlayback(\"MIDI file test stopped by panic.", StringComparison.Ordinal), "MIDI panic should stop active file-test playback before resetting output");
    Assert(windowSource.Contains("StopSoundFontSamplePreview", StringComparison.Ordinal), "SoundFont sample preview should be disposed during MIDI cleanup");
    Assert(windowSource.Contains("Select an incoming MIDI message before mapping.", StringComparison.Ordinal), "MIDI mapping workflow should reject outbound monitor messages");
    Assert(windowSource.Contains("if (message.Channel is null)", StringComparison.Ordinal), "MIDI mapping workflow should reject channel-less system messages");
    Assert(windowSource.Contains("GetTriggeredMappings", StringComparison.Ordinal), "MIDI mapping workflow should use edge-gated trigger state");
    Assert(windowSource.Contains("UpdateMidiDeviceDetails", StringComparison.Ordinal), "MIDI workflow should surface selected device diagnostics");
    Assert(windowSource.Contains("UpdateSelectedMidiMessageDetails", StringComparison.Ordinal), "MIDI workflow should surface selected message diagnostics");
    Assert(windowSource.Contains("SendMidiBatchMessages", StringComparison.Ordinal), "MIDI workflow should monitor multi-channel reset sends");
    Assert(windowSource.Contains("Task.Run(async () =>", StringComparison.Ordinal), "MIDI file-test playback should send timed events off the UI thread");
    Assert(windowSource.Contains("PostMidiMessage", StringComparison.Ordinal), "MIDI file-test playback should marshal monitor updates safely");
    var expectedSectionOrder = new[]
    {
        "1 Device Check",
        "2 Live Monitor",
        "3 Utility Mapping",
        "4 MIDI File Test",
        "5 SoundFont Inspector",
        "6 Message Sender"
    };
    var lastSectionIndex = xaml.IndexOf("Header=\"MIDI\"", StringComparison.Ordinal);
    foreach (var section in expectedSectionOrder)
    {
        var sectionIndex = xaml.IndexOf(section, lastSectionIndex + 1, StringComparison.Ordinal);
        Assert(sectionIndex > lastSectionIndex, $"MIDI tab should keep {section} after the previous section");
        lastSectionIndex = sectionIndex;
    }

    Assert(MidiDeviceCatalog.GetInputDevices() is not null, "MIDI input enumeration should be safe without attached hardware");
    Assert(MidiDeviceCatalog.GetOutputDevices() is not null, "MIDI output enumeration should be safe without attached hardware");
}

static void NAudioMidiMessageUtilitiesClampAndParseSafely()
{
    var rawNote = MidiOutputPort.CreateNoteOnRawMessage(0, 200, -10);
    var note = MidiMessageSnapshot.FromRaw(rawNote, 123, "Out");
    Assert(note.Channel == 1, "MIDI channels should clamp to channel 1 minimum");
    Assert(note.Data1 == 127, "MIDI note values should clamp to 7-bit maximum");
    Assert(note.Data2 == 0, "MIDI velocity values should clamp to 7-bit minimum");

    var rawPitch = MidiOutputPort.CreatePitchWheelRawMessage(20, 20000);
    var pitch = MidiMessageSnapshot.FromRaw(rawPitch, 456, "Out");
    Assert(pitch.Channel == 16, "MIDI channels should clamp to channel 16 maximum");
    Assert(pitch.Data1 == 127 && pitch.Data2 == 127, "MIDI pitch wheel should clamp to 14-bit maximum");

    var bankSelect = MidiOutputPort.CreateBankSelectRawMessages(1, 130);
    Assert(bankSelect.Count == 2, "MIDI bank select should send MSB and LSB controller messages");
    var bankMsb = MidiMessageSnapshot.FromRaw(bankSelect[0], 321, "Out");
    var bankLsb = MidiMessageSnapshot.FromRaw(bankSelect[1], 322, "Out");
    Assert(bankMsb.Data1 == (int)MidiController.BankSelect && bankMsb.Data2 == 1, "MIDI bank select MSB should carry the high bank bits");
    Assert(bankLsb.Data1 == (int)MidiController.BankSelectLsb && bankLsb.Data2 == 2, "MIDI bank select LSB should carry the low bank bits");

    Assert(MidiHexParser.TryParseShortMessage("90 3C 7F", out var parsed), "MIDI short hex parser should accept spaced bytes");
    var parsedSnapshot = MidiMessageSnapshot.FromRaw(parsed, 789);
    Assert(parsedSnapshot.MessageType == "Note On", "parsed MIDI status should identify note on");
    Assert(parsedSnapshot.Channel == 1 && parsedSnapshot.Data1 == 60 && parsedSnapshot.Data2 == 127, "parsed MIDI data bytes should be ordered as status/data1/data2");
    Assert(parsedSnapshot.Details.Contains("C4", StringComparison.Ordinal), "MIDI monitor details should decode note names for troubleshooting");
    Assert(parsedSnapshot.InspectionText.Contains("raw 90 3C 7F", StringComparison.Ordinal), "MIDI message inspection should include raw hex bytes");
    Assert(MidiMessageSnapshot.FormatControllerName(123) == "All Notes Off", "MIDI controller labels should identify all-notes-off messages");

    var allNotesOff = MidiOutputPort.CreateAllNotesOffRawMessages();
    Assert(allNotesOff.Count == 16, "all-notes-off should target every MIDI channel");
    var allNotesOffFirst = MidiMessageSnapshot.FromRaw(allNotesOff[0], 900, "Out");
    var allNotesOffLast = MidiMessageSnapshot.FromRaw(allNotesOff[^1], 901, "Out");
    Assert(allNotesOffFirst.Channel == 1 && allNotesOffFirst.Data1 == 123, "all-notes-off should start on channel 1 with controller 123");
    Assert(allNotesOffLast.Channel == 16 && allNotesOffLast.Data1 == 123, "all-notes-off should include channel 16 with controller 123");
    var resetControllers = MidiOutputPort.CreateResetAllControllersRawMessages();
    Assert(resetControllers.Count == 16, "controller reset should target every MIDI channel");
    Assert(MidiMessageSnapshot.FromRaw(resetControllers[0], 902, "Out").Data1 == 121, "controller reset should use controller 121");

    Assert(MidiHexParser.TryParseBytes("F0 7E 00 F7", out var sysex), "MIDI hex parser should accept sysex byte streams");
    var sysexSnapshot = MidiMessageSnapshot.FromSysex(sysex, 999);
    Assert(sysexSnapshot.SysexBytes?.Length == 4, "MIDI sysex snapshots should preserve byte buffers");
    Assert(!MidiHexParser.TryParseShortMessage("90 3C 7F 00", out _), "MIDI short parser should reject messages over three bytes");
}

static void MidiControlMappingsMatchIncomingChannelMessages()
{
    var raw = MidiOutputPort.CreateControlChangeRawMessage(2, 64, 127);
    var snapshot = MidiMessageSnapshot.FromRaw(raw, 123);
    var rule = MidiControlMappingRule.FromMessage(snapshot, MidiControlMappingActions.ToggleSelectedInputMute);

    Assert(rule.Matches(snapshot), "MIDI mapping should match the source channel message");
    Assert(rule.ShouldTrigger(snapshot), "MIDI mapping should trigger on active control messages");
    Assert(rule.DisplayName.Contains(MidiControlMappingActions.ToggleSelectedInputMute, StringComparison.Ordinal), "MIDI mapping display should include the action");
    Assert(rule.Details.Contains("channel 2", StringComparison.Ordinal), "MIDI mapping details should include the channel");

    var differentController = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateControlChangeRawMessage(2, 65, 127), 124);
    Assert(!rule.Matches(differentController), "MIDI mapping should not match a different controller");
    var releasedController = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateControlChangeRawMessage(2, 64, 0), 125);
    Assert(!rule.ShouldTrigger(releasedController), "MIDI mapping should ignore control release messages");
    var noteOffRule = MidiControlMappingRule.FromMessage(MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateNoteOffRawMessage(1, 60, 0), 126), MidiControlMappingActions.ToggleSelectedInputMute);
    Assert(!noteOffRule.ShouldTrigger(MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateNoteOffRawMessage(1, 60, 0), 127)), "MIDI mapping should ignore note off messages");
    var patchSnapshot = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreatePatchChangeRawMessage(3, 12), 128);
    var patchRule = MidiControlMappingRule.FromMessage(patchSnapshot, MidiControlMappingActions.ToggleProcessedOutput);
    Assert(patchRule.ShouldTrigger(patchSnapshot), "MIDI mapping should allow patch change messages even though their second data byte is zero");
    Assert(MidiControlMappingActions.DefaultActions.Contains(MidiControlMappingActions.ToggleProcessedOutput), "MIDI mapping actions should include processed output routing");
    Assert(MidiControlMappingActions.DefaultActions.Contains(MidiControlMappingActions.SendAllNotesOff), "MIDI mapping actions should include all-notes-off troubleshooting");
    Assert(MidiControlMappingActions.DefaultActions.Contains(MidiControlMappingActions.ResetMidiControllers), "MIDI mapping actions should include controller reset troubleshooting");
}

static void MidiControlMappingTriggerStateIsEdgeGated()
{
    var muteRule = new MidiControlMappingRule(MidiControlMappingActions.ToggleSelectedInputMute, "Control Change", 2, 64);
    var soloRule = new MidiControlMappingRule(MidiControlMappingActions.ToggleSelectedInputSolo, "Control Change", 2, 64);
    var patchRule = new MidiControlMappingRule(MidiControlMappingActions.ToggleProcessedOutput, "Patch Change", 3, 12);
    var triggerState = new MidiControlMappingTriggerState();
    var mappings = new[] { muteRule, soloRule, patchRule };

    var press = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateControlChangeRawMessage(2, 64, 127), 100);
    var firstPress = triggerState.GetTriggeredMappings(mappings, press);
    Assert(firstPress.Count == 2, "first MIDI control press should trigger every mapped action for that control");
    Assert(firstPress[0] == muteRule && firstPress[1] == soloRule, "MIDI control mappings should preserve mapping order");

    var repeatedPress = triggerState.GetTriggeredMappings(mappings, press);
    Assert(repeatedPress.Count == 0, "held MIDI control messages should not retrigger toggle actions");

    var release = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateControlChangeRawMessage(2, 64, 0), 101);
    Assert(triggerState.GetTriggeredMappings(mappings, release).Count == 0, "MIDI control release should only re-arm the mapping");
    Assert(triggerState.GetTriggeredMappings(mappings, press).Count == 2, "MIDI control press should trigger again after release");

    var noteRule = new MidiControlMappingRule(MidiControlMappingActions.ToggleSelectedInputMute, "Note On", 1, 60);
    var noteState = new MidiControlMappingTriggerState();
    var notePress = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateNoteOnRawMessage(1, 60, 100), 102);
    var noteOff = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreateNoteOffRawMessage(1, 60, 0), 103);
    Assert(noteState.GetTriggeredMappings([noteRule], notePress).Count == 1, "MIDI note-on should trigger mappings");
    Assert(noteState.GetTriggeredMappings([noteRule], notePress).Count == 0, "held MIDI notes should not retrigger mappings");
    Assert(noteState.GetTriggeredMappings([noteRule], noteOff).Count == 0, "MIDI note-off should only re-arm note mappings");
    Assert(noteState.GetTriggeredMappings([noteRule], notePress).Count == 1, "MIDI note-on should trigger again after note-off");

    var patchSnapshot = MidiMessageSnapshot.FromRaw(MidiOutputPort.CreatePatchChangeRawMessage(3, 12), 104);
    Assert(triggerState.GetTriggeredMappings(mappings, patchSnapshot).Single() == patchRule, "non-momentary MIDI messages should trigger matching mappings");
    Assert(triggerState.GetTriggeredMappings(mappings, patchSnapshot).Single() == patchRule, "non-momentary MIDI messages should not be edge gated");
}

static void MidiSequencePlaybackPlanSchedulesTempoAwareEvents()
{
    var events = new MidiEventCollection(1, 480);
    events.AddEvent(new TempoEvent(500_000, 0), 0);
    events.AddEvent(new PatchChangeEvent(0, 1, 7), 0);
    events.AddEvent(new NoteOnEvent(480, 1, 60, 100, 240), 0);
    events.AddEvent(new NoteEvent(720, 1, MidiCommandCode.NoteOff, 60, 0), 0);
    events.AddEvent(new TempoEvent(1_000_000, 960), 0);
    var sysexEvent = new SysexEvent { AbsoluteTime = 1200 };
    typeof(SysexEvent).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sysexEvent, new byte[] { 0xF0, 0x7E, 0x00, 0xF7 });
    events.AddEvent(sysexEvent, 0);
    events.AddEvent(new ControlChangeEvent(1440, 1, MidiController.Modulation, 64), 0);

    var plan = MidiSequenceService.CreatePlaybackPlan(events, "fixture.mid");

    Assert(plan.FileName == "fixture.mid", "MIDI playback plan should retain the file name");
    Assert(plan.Events.Count == 5, "MIDI playback plan should include playable channel and sysex events");
    Assert(plan.Events[0].Offset == TimeSpan.Zero, "MIDI patch should play at the start");
    Assert(plan.Events[1].Offset == TimeSpan.FromMilliseconds(500), "MIDI note on should follow the initial tempo");
    Assert(plan.Events[2].Offset == TimeSpan.FromMilliseconds(750), "MIDI note off should follow the initial tempo");
    Assert(plan.Events[3].Offset == TimeSpan.FromMilliseconds(1500), "MIDI sysex should follow the slower tempo after the tempo event");
    Assert(plan.Events[4].Offset == TimeSpan.FromMilliseconds(2000), "MIDI control change should follow the slower tempo after the tempo event");
    Assert(plan.Events[0].RawMessage == MidiOutputPort.CreatePatchChangeRawMessage(1, 7), "MIDI sequence patch raw message should match the output helper");
    Assert(plan.Events[1].RawMessage == MidiOutputPort.CreateNoteOnRawMessage(1, 60, 100), "MIDI sequence note raw message should match the output helper");
    Assert(plan.Events[3].SysexBytes?.Length == 4, "MIDI sequence sysex payload should be preserved");
    Assert(plan.Events[3].Details.Contains("F0 7E 00 F7", StringComparison.Ordinal), "MIDI sequence sysex display should show payload bytes");
    Assert(plan.DisplayText.Contains("5 playable events", StringComparison.Ordinal), "MIDI playback plan display should name playable event count");
}

static void SoundFontSamplePreviewCopiesPcmSlicesSafely()
{
    var source = new byte[]
    {
        0x00, 0x10,
        0x01, 0x11,
        0x02, 0x12,
        0x03, 0x13,
        0x04, 0x14
    };

    var preview = SoundFontLibrary.CopySamplePcm16(source, startSample: 1, endSample: 4);
    AssertSequenceEqual(new byte[] { 0x01, 0x11, 0x02, 0x12, 0x03, 0x13 }, preview, "SoundFont preview should copy the requested PCM16 sample range");
    preview[0] = 0xFF;
    Assert(source[2] == 0x01, "SoundFont preview should copy data instead of aliasing the source buffer");
    var cappedPreview = SoundFontLibrary.CopySamplePcm16(source, startSample: 0, endSample: 5, maxSampleCount: 2);
    AssertSequenceEqual(new byte[] { 0x00, 0x10, 0x01, 0x11 }, cappedPreview, "SoundFont preview should cap long samples to the requested preview length");
    AssertThrows<InvalidOperationException>(
        () => SoundFontLibrary.CopySamplePcm16(source, startSample: 3, endSample: 3),
        "empty SoundFont samples should be rejected");
    AssertThrows<InvalidOperationException>(
        () => SoundFontLibrary.CopySamplePcm16(source, startSample: 4, endSample: 8),
        "out-of-range SoundFont samples should be rejected");
    AssertThrows<ArgumentOutOfRangeException>(
        () => SoundFontLibrary.CopySamplePcm16(source, startSample: 0, endSample: 5, maxSampleCount: 0),
        "invalid SoundFont preview caps should be rejected");
}

static void VoiceBreathReducerTamesAiryBreathNoise()
{
    const int sampleRate = 48_000;
    var breathTone = GenerateSine(sampleRate, 6_500d, 0.24d, 2.0d);
    var voiceTone = GenerateSine(sampleRate, 1_000d, 0.35d, 2.0d);
    var breathBypass = ProcessBreathReducerTestTone(breathTone, enabled: false);
    var breathReduced = ProcessBreathReducerTestTone(breathTone, enabled: true);
    var voiceBypass = ProcessBreathReducerTestTone(voiceTone, enabled: false);
    var voiceReduced = ProcessBreathReducerTestTone(voiceTone, enabled: true);

    var start = sampleRate;
    var breathBypassRms = CalculateTailRms(breathBypass, start);
    var breathReducedRms = CalculateTailRms(breathReduced, start);
    var voiceBypassRms = CalculateTailRms(voiceBypass, start);
    var voiceReducedRms = CalculateTailRms(voiceReduced, start);

    Assert(breathReducedRms < breathBypassRms * 0.72d, "breath reducer should lower airy breath-band noise");
    Assert(voiceReducedRms > voiceBypassRms * 0.86d, "breath reducer should preserve normal voice tone");
    Assert(breathReduced.All(float.IsFinite) && voiceReduced.All(float.IsFinite), "breath reducer output should stay finite");
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

static void EqScreenBindsEveryVoiceProcessorSetting()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var missing = GetVoiceProcessorDspSettingProperties()
        .Where(property => !HasDirectBinding(xaml, property.Name))
        .Select(property => property.Name)
        .ToArray();

    Assert(missing.Length == 0, $"EQ screen is missing DSP bindings: {string.Join(", ", missing)}");
}

static void MainMenuExposesGlobalDeviceAndHelpActions()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(xaml.Contains("Header=\"_File\"", StringComparison.Ordinal), "main window should expose a File menu");
    Assert(xaml.Contains("Header=\"Refresh Audio Devices\"", StringComparison.Ordinal), "File menu should expose Refresh Audio Devices");
    Assert(xaml.Contains("Click=\"RefreshAudioDevicesMenuClicked\"", StringComparison.Ordinal), "Refresh Audio Devices should be wired to a handler");
    Assert(xaml.Contains("Header=\"Refresh Video Devices\"", StringComparison.Ordinal), "File menu should expose Refresh Video Devices");
    Assert(xaml.Contains("Click=\"RefreshVideoDevicesMenuClicked\"", StringComparison.Ordinal), "Refresh Video Devices should be wired to a handler");
    Assert(xaml.Contains("x:Name=\"EnableMidiMenuItem\"", StringComparison.Ordinal), "File menu should expose an Enable MIDI toggle");
    Assert(xaml.Contains("Header=\"Enable MIDI\"", StringComparison.Ordinal), "Enable MIDI toggle should use the requested label");
    Assert(xaml.Contains("IsCheckable=\"True\"", StringComparison.Ordinal), "Enable MIDI should be a checkbox-style menu item");
    Assert(xaml.Contains("Checked=\"EnableMidiChanged\"", StringComparison.Ordinal), "Enable MIDI checked state should be wired to a handler");
    Assert(xaml.Contains("Header=\"Settings\"", StringComparison.Ordinal), "File menu should expose a Settings submenu");
    Assert(xaml.Contains("Header=\"Podcast Settings\"", StringComparison.Ordinal), "Settings submenu should expose Podcast Settings");
    Assert(xaml.Contains("Click=\"PodcastSettingsMenuClicked\"", StringComparison.Ordinal), "Podcast Settings should be wired to a handler");
    Assert(xaml.Contains("Header=\"Karaoke Settings\"", StringComparison.Ordinal), "Settings submenu should expose Karaoke Settings");
    Assert(xaml.Contains("Click=\"KaraokeSettingsMenuClicked\"", StringComparison.Ordinal), "Karaoke Settings should be wired to a handler");
    Assert(xaml.Contains("Header=\"Audio Device Diagnostics\"", StringComparison.Ordinal), "Settings submenu should expose Audio Device Diagnostics");
    Assert(xaml.Contains("Click=\"AudioDeviceDiagnosticsMenuClicked\"", StringComparison.Ordinal), "Audio Device Diagnostics should be wired to a handler");
    Assert(xaml.Contains("Header=\"ASIO Settings\"", StringComparison.Ordinal), "File menu should expose ASIO Settings");
    Assert(xaml.Contains("Header=\"ASIO Callback Test\"", StringComparison.Ordinal), "File menu should expose the ASIO callback test");
    Assert(xaml.Contains("Click=\"AsioCallbackTestMenuClicked\"", StringComparison.Ordinal), "ASIO callback test should be wired to a handler");
    Assert(xaml.Contains("Header=\"_Help\"", StringComparison.Ordinal), "main window should expose a Help menu");
    Assert(xaml.Contains("Header=\"Podcast\"", StringComparison.Ordinal), "Help menu should expose the Podcast guide");
    Assert(xaml.Contains("Click=\"PodcastHelpMenuClicked\"", StringComparison.Ordinal), "Podcast guide should be wired to a handler");
    Assert(xaml.Contains("Header=\"Karaoke\"", StringComparison.Ordinal), "Help menu should expose the Karaoke guide");
    Assert(xaml.Contains("Click=\"KaraokeHelpMenuClicked\"", StringComparison.Ordinal), "Karaoke guide should be wired to a handler");
    Assert(xaml.Contains("Header=\"Mic / DSP\"", StringComparison.Ordinal), "Help menu should expose the Mic / DSP guide");
    Assert(xaml.Contains("Click=\"MicDspHelpMenuClicked\"", StringComparison.Ordinal), "Mic / DSP guide should be wired to a handler");
    Assert(xaml.Contains("Header=\"Mixing\"", StringComparison.Ordinal), "Help menu should expose the Mixing guide");
    Assert(xaml.Contains("Click=\"MixingHelpMenuClicked\"", StringComparison.Ordinal), "Mixing guide should be wired to a handler");
    Assert(xaml.Contains("Header=\"MIDI\"", StringComparison.Ordinal), "Help menu should expose the MIDI guide");
    Assert(xaml.Contains("Click=\"MidiHelpMenuClicked\"", StringComparison.Ordinal), "MIDI guide should be wired to a handler");
    Assert(!xaml.Contains("Header=\"Tabs and Features\"", StringComparison.Ordinal), "Help menu should not expose a grouped tab guide");
    Assert(!xaml.Contains("TabsHelpMenuClicked", StringComparison.Ordinal), "grouped tab guide handler should not be wired");
    Assert(xaml.Contains("Header=\"About\"", StringComparison.Ordinal), "Help menu should expose About");
    Assert(xaml.Contains("Header=\"Verification\"", StringComparison.Ordinal), "About menu should expose DSP Verification");
    Assert(xaml.Contains("Click=\"VerificationMenuClicked\"", StringComparison.Ordinal), "DSP Verification should be wired to a handler");
    Assert(xaml.Contains("SystemColors.MenuHighlightBrushKey", StringComparison.Ordinal), "main menu should override bright system highlight colors");
    Assert(xaml.Contains("PART_Popup", StringComparison.Ordinal), "main menu should use a custom readable dark popup template");
    Assert(xaml.Contains("Color=\"#1D1D1D\"", StringComparison.Ordinal), "main menu popup should use the dark menu background");
    Assert(xaml.Contains("Color=\"#7E858C\"", StringComparison.Ordinal), "main menu disabled text should remain readable");
    Assert(!xaml.Contains("<TabItem Header=\"About\"", StringComparison.Ordinal), "About should live under Help instead of the main tab strip");
    Assert(!xaml.Contains("RecordRawBackupCheckBox", StringComparison.Ordinal), "Podcast recording UI should not expose a raw-backup checkbox unless it is wired to recording behavior");
    var project = File.ReadAllText(FindRepoFile("JerichoDown.csproj"));
    string[] tabGuideFiles =
    [
        "jericho-down-podcast-guide.pdf",
        "jericho-down-karaoke-guide.pdf",
        "jericho-down-mic-dsp-guide.pdf",
        "jericho-down-mixing-guide.pdf",
        "jericho-down-midi-guide.pdf"
    ];
    foreach (var guideFile in tabGuideFiles)
    {
        Assert(project.Contains($"Docs\\{guideFile}", StringComparison.Ordinal), $"{guideFile} should be copied to output");
        Assert(File.Exists(FindRepoFile(Path.Combine("Docs", guideFile))), $"{guideFile} should exist");
    }

    Assert(!project.Contains("Docs\\jericho-down-tabs-guide.pdf", StringComparison.Ordinal), "grouped tab guide PDF should not be copied to output");
    Assert(File.ReadAllText(FindRepoFile(".gitattributes")).Contains("*.pdf binary", StringComparison.Ordinal), "PDF guides should be treated as binary files");
    Assert(File.ReadAllText(FindRepoFile("AboutView.xaml")).Contains("About Jericho Down", StringComparison.Ordinal), "About popup should preserve the previous About content");
    Assert(File.ReadAllText(FindRepoFile("VerificationView.xaml")).Contains("DSP Verification", StringComparison.Ordinal), "Verification popup should preserve customer-facing proof content");
}

static void PodcastSessionPlaybackPrefersDx12FileRenderer()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var playbackService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "SessionPlayback", "MediaFoundationFilePlaybackService.cs")));
    var interop = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationInterop.cs")));

    Assert(xaml.Contains("x:Name=\"SessionDx12PlaybackHostPanel\"", StringComparison.Ordinal), "Podcast tab should reserve a DX12 host for session playback");
    Assert(xaml.Contains("x:Name=\"SessionPlaybackElement\"", StringComparison.Ordinal), "legacy MediaElement fallback should remain available");
    Assert(windowCode.Contains("using JerichoDown.Modules.SessionPlayback;", StringComparison.Ordinal), "AppShell should reference the SessionPlayback module namespace explicitly");
    Assert(windowCode.Contains("new Direct3D12PreviewHost", StringComparison.Ordinal), "session playback should render through the existing DX12 swap-chain host");
    Assert(windowCode.Contains("new MediaFoundationFilePlaybackService()", StringComparison.Ordinal), "session playback should use the Media Foundation file reader service");
    Assert(playbackService.Contains("namespace JerichoDown.Modules.SessionPlayback;", StringComparison.Ordinal), "file playback service should live in the SessionPlayback module namespace");
    Assert(windowCode.Contains("ResolveSessionAudioPlaybackPath(path)", StringComparison.Ordinal), "DX12 video playback should resolve the matching session sidecar audio");
    Assert(windowCode.Contains("$\"mix_{number}.wav\"", StringComparison.Ordinal), "session playback should prefer the recorded mix WAV beside the MP4");
    Assert(windowCode.Contains("$\"raw_backup_{number}.wav\"", StringComparison.Ordinal), "session playback should fall back to the raw backup WAV when a mix is missing");
    Assert(windowCode.Contains("new AudioFileReader(audioPath)", StringComparison.Ordinal), "DX12 video playback should keep resolved session audio routed through NAudio");
    Assert(windowCode.Contains("CreateSelectedPlaybackOutput(reader.ToWaveProvider()", StringComparison.Ordinal), "session audio should use the selected Jericho output route");
    Assert(windowCode.Contains("TryStartSessionSidecarAudioPlayback(path", StringComparison.Ordinal), "Windows media fallback should still play sidecar audio for podcast session videos");

    var startMethod = ExtractSourceBetween(
        windowCode,
        "    private void StartSessionPlayback(string path)",
        "    private bool TryStartDx12SessionPlayback");
    var dx12Index = startMethod.IndexOf("TryStartDx12SessionPlayback(path", StringComparison.Ordinal);
    var fallbackIndex = startMethod.IndexOf("StartSessionMediaElementFallbackPlayback(path)", StringComparison.Ordinal);
    Assert(dx12Index >= 0 && fallbackIndex > dx12Index, "session playback should try DX12 before Windows media fallback");
    Assert(startMethod.Contains("_isCameraEnabled = false;", StringComparison.Ordinal), "session playback should not leave the webcam marked live while the file player owns the preview surface");

    Assert(playbackService.Contains("MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS", StringComparison.Ordinal), "file playback should request Media Foundation hardware transforms");
    Assert(playbackService.Contains("MFCreateSourceReaderFromURL(path, attributes", StringComparison.Ordinal), "file playback should open MP4 sessions with a source reader");
    Assert(playbackService.Contains("MFVideoFormat_RGB32", StringComparison.Ordinal), "file playback should decode to a DX12-uploadable BGRA path");
    Assert(playbackService.Contains("MFVideoFormat_NV12", StringComparison.Ordinal), "file playback should keep an NV12 fallback for efficient video frames");
    Assert(playbackService.Contains("ResolvePresentationTicks(", StringComparison.Ordinal), "file playback should normalize or synthesize presentation timestamps");
    Assert(playbackService.Contains("sourceDeltaTicks >= minimumSourceDeltaTicks", StringComparison.Ordinal), "file playback should reject implausibly tiny camera timestamps that make real-camera recordings race");
    Assert(playbackService.Contains("syntheticPresentationTicks = syntheticTicks + frameDurationTicks", StringComparison.Ordinal), "file playback should pace bad-timestamp frames from the decoded frame rate");
    Assert(interop.Contains("MFCreateSourceReaderFromURL", StringComparison.Ordinal), "Media Foundation interop should expose file source readers");
}

static void ModuleReadmesDefineOwnership()
{
    var rootReadme = File.ReadAllText(FindRepoFile("README.md"));
    var moduleIndex = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "README.md")));
    string[] moduleReadmes =
    [
        Path.Combine("Modules", "AppShell", "README.md"),
        Path.Combine("Modules", "Audio", "README.md"),
        Path.Combine("Modules", "Audio", "Asio", "README.md"),
        Path.Combine("Modules", "Audio", "Dsp", "README.md"),
        Path.Combine("Modules", "Mixer", "README.md"),
        Path.Combine("Modules", "Webcam", "README.md"),
        Path.Combine("Modules", "Webcam", "MediaFoundation", "README.md"),
        Path.Combine("Modules", "Webcam", "DirectShow", "README.md"),
        Path.Combine("Modules", "Webcam", "Dx12", "README.md"),
        Path.Combine("Modules", "Webcam", "Dx11Bridge", "README.md"),
        Path.Combine("Modules", "SessionPlayback", "README.md"),
        Path.Combine("Modules", "Karaoke", "README.md"),
        Path.Combine("Modules", "Midi", "README.md"),
        Path.Combine("Modules", "Help", "README.md"),
        Path.Combine("Modules", "Visualization", "README.md"),
        Path.Combine("Modules", "Visualization", "Dx12", "README.md")
    ];

    Assert(rootReadme.Contains("[Modules](Modules/README.md)", StringComparison.Ordinal), "root README should point maintainers at the module map");
    Assert(moduleIndex.Contains("move one small ownership boundary at a time", StringComparison.OrdinalIgnoreCase), "module index should preserve the safe migration rule");
    Assert(moduleIndex.Contains("SessionPlayback", StringComparison.Ordinal), "module index should list session playback ownership");
    Assert(moduleIndex.Contains("Webcam/Dx12", StringComparison.Ordinal), "module index should list DX12 webcam ownership");
    Assert(moduleIndex.Contains("Webcam` owns `CameraStatusText` and `VideoRecordingPolicy`", StringComparison.Ordinal), "module index should record migrated webcam status/policy helpers");
    Assert(moduleIndex.Contains("Webcam` owns `CameraDevice`, `CameraVideoMode`, `CameraFrame`, `CameraControlKind`, and `CameraControlItem`", StringComparison.Ordinal), "module index should record migrated webcam vocabulary helpers");
    Assert(moduleIndex.Contains("Webcam` owns `CameraDeviceCatalog`, `CameraControlText`, `CameraProfile`, and `CameraProfileStore`", StringComparison.Ordinal), "module index should record migrated webcam catalog/profile helpers");
    Assert(moduleIndex.Contains("Webcam` owns `CameraSourceSelection` and `TextureNativePreviewPolicy`", StringComparison.Ordinal), "module index should record migrated webcam selection/policy helpers");
    Assert(moduleIndex.Contains("Webcam/MediaFoundation` owns `MediaFoundationGuids` and `MediaFoundationInterop`", StringComparison.Ordinal), "module index should record migrated Media Foundation interop ownership");
    Assert(moduleIndex.Contains("Webcam/MediaFoundation` owns `MediaFoundationCameraEnumerator`, `MediaFoundationCameraModeService`, `MediaFoundationCameraDeviceFactory`, `MediaFoundationVideoRecorder`, and `MediaFoundationCameraPreviewService`", StringComparison.Ordinal), "module index should record migrated Media Foundation discovery/factory/writer/preview ownership");
    Assert(moduleIndex.Contains("Webcam/DirectShow` owns `DirectShowCameraEnumerator`, `DirectShowCameraControlService`, and `DirectShowCameraPreviewService`", StringComparison.Ordinal), "module index should record migrated DirectShow discovery/control/preview ownership");
    Assert(moduleIndex.Contains("Webcam/Dx11Bridge` owns `Direct3D11DeviceManager` and `Direct3D11SharedTextureBridge`", StringComparison.Ordinal), "module index should record migrated DX11 bridge ownership");
    Assert(moduleIndex.Contains("Webcam/Dx12` owns `Direct3D12DeviceManager`, `ITextureNativeDeviceManager`, `Direct3D12PreviewHost`, `Dx12Camera`, `Dx12CameraOptions`, `CameraPreviewFramePumps`, `TextureNativeCameraRecorder`, and `TextureNativeCameraProbe`", StringComparison.Ordinal), "module index should record migrated DX12 camera ownership");
    Assert(moduleIndex.Contains("Visualization/Dx12` owns `Direct3D12AudioGraphHost` and `Direct3D12AudioGraphMode`", StringComparison.Ordinal), "module index should record migrated DX12 audio graph ownership");
    Assert(moduleIndex.Contains("Audio/Asio` owns `AsioInputCapture`, `AsioCallbackProbe`, `AsioOutputPlayer`, and `StaThreadDispatcher`", StringComparison.Ordinal), "module index should record migrated ASIO ownership");
    Assert(moduleIndex.Contains("Midi` owns `MidiDeviceCatalog`, `MidiFileService`, `MidiHexParser`, `MidiInputMonitor`, `MidiMessageSnapshot`, `MidiOutputPort`, `MidiSequenceService`, MIDI control mappings, and `SoundFontLibrary`", StringComparison.Ordinal), "module index should record migrated MIDI ownership");

    foreach (var readmePath in moduleReadmes)
    {
        var text = File.ReadAllText(FindRepoFile(readmePath));
        Assert(text.Contains("Owns", StringComparison.OrdinalIgnoreCase) || text.Contains("Responsibilities", StringComparison.OrdinalIgnoreCase), $"{readmePath} should define ownership or responsibilities");
    }

    var sessionPlaybackReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "SessionPlayback", "README.md")));
    Assert(sessionPlaybackReadme.Contains("mix_###.wav", StringComparison.Ordinal), "session playback docs should preserve sidecar audio behavior");
    Assert(sessionPlaybackReadme.Contains("raw_backup_###.wav", StringComparison.Ordinal), "session playback docs should preserve raw backup fallback behavior");

    var audioAsioReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "README.md")));
    var asioInputCapture = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "AsioInputCapture.cs")));
    var asioCallbackProbe = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "AsioCallbackProbe.cs")));
    var asioOutputPlayer = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "AsioOutputPlayer.cs")));
    var staThreadDispatcher = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "StaThreadDispatcher.cs")));
    Assert(audioAsioReadme.Contains("AsioInputCapture.cs", StringComparison.Ordinal), "ASIO docs should name migrated input capture ownership");
    Assert(audioAsioReadme.Contains("AsioCallbackProbe.cs", StringComparison.Ordinal), "ASIO docs should name migrated callback probe ownership");
    Assert(audioAsioReadme.Contains("AsioOutputPlayer.cs", StringComparison.Ordinal), "ASIO docs should name migrated output player ownership");
    Assert(audioAsioReadme.Contains("StaThreadDispatcher.cs", StringComparison.Ordinal), "ASIO docs should name migrated STA dispatcher ownership");
    Assert(asioInputCapture.Contains("namespace JerichoDown.Modules.Audio.Asio;", StringComparison.Ordinal), "ASIO input capture should live in the Audio ASIO module namespace");
    Assert(asioCallbackProbe.Contains("namespace JerichoDown.Modules.Audio.Asio;", StringComparison.Ordinal), "ASIO callback probe should live in the Audio ASIO module namespace");
    Assert(asioOutputPlayer.Contains("namespace JerichoDown.Modules.Audio.Asio;", StringComparison.Ordinal), "ASIO output player should live in the Audio ASIO module namespace");
    Assert(staThreadDispatcher.Contains("namespace JerichoDown.Modules.Audio.Asio;", StringComparison.Ordinal), "ASIO STA dispatcher should live in the Audio ASIO module namespace");

    var midiReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "README.md")));
    var midiDeviceCatalog = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiDeviceCatalog.cs")));
    var midiFileService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiFileService.cs")));
    var midiHexParser = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiHexParser.cs")));
    var midiInputMonitor = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiInputMonitor.cs")));
    var midiMessageSnapshot = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiMessageSnapshot.cs")));
    var midiOutputPort = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiOutputPort.cs")));
    var midiSequenceService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiSequenceService.cs")));
    var midiControlMappingRule = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiControlMappingRule.cs")));
    var midiControlMappingTriggerState = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "MidiControlMappingTriggerState.cs")));
    var soundFontLibrarySource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Midi", "SoundFontLibrary.cs")));
    Assert(midiReadme.Contains("MidiDeviceCatalog.cs", StringComparison.Ordinal), "MIDI docs should name migrated device catalog ownership");
    Assert(midiReadme.Contains("MidiFileService.cs", StringComparison.Ordinal), "MIDI docs should name migrated file service ownership");
    Assert(midiReadme.Contains("MidiHexParser.cs", StringComparison.Ordinal), "MIDI docs should name migrated hex parser ownership");
    Assert(midiReadme.Contains("MidiInputMonitor.cs", StringComparison.Ordinal), "MIDI docs should name migrated input monitor ownership");
    Assert(midiReadme.Contains("MidiMessageSnapshot.cs", StringComparison.Ordinal), "MIDI docs should name migrated message snapshot ownership");
    Assert(midiReadme.Contains("MidiOutputPort.cs", StringComparison.Ordinal), "MIDI docs should name migrated output port ownership");
    Assert(midiReadme.Contains("MidiSequenceService.cs", StringComparison.Ordinal), "MIDI docs should name migrated sequence service ownership");
    Assert(midiReadme.Contains("MidiControlMappingRule.cs", StringComparison.Ordinal), "MIDI docs should name migrated control mapping rule ownership");
    Assert(midiReadme.Contains("MidiControlMappingTriggerState.cs", StringComparison.Ordinal), "MIDI docs should name migrated control mapping trigger ownership");
    Assert(midiReadme.Contains("SoundFontLibrary.cs", StringComparison.Ordinal), "MIDI docs should name migrated SoundFont ownership");
    Assert(midiDeviceCatalog.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI device catalog should live in the MIDI module namespace");
    Assert(midiFileService.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI file service should live in the MIDI module namespace");
    Assert(midiHexParser.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI hex parser should live in the MIDI module namespace");
    Assert(midiInputMonitor.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI input monitor should live in the MIDI module namespace");
    Assert(midiMessageSnapshot.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI message snapshot should live in the MIDI module namespace");
    Assert(midiOutputPort.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI output port should live in the MIDI module namespace");
    Assert(midiSequenceService.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI sequence service should live in the MIDI module namespace");
    Assert(midiControlMappingRule.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI control mapping rule should live in the MIDI module namespace");
    Assert(midiControlMappingTriggerState.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "MIDI control mapping trigger state should live in the MIDI module namespace");
    Assert(soundFontLibrarySource.Contains("namespace JerichoDown.Modules.Midi;", StringComparison.Ordinal), "SoundFont library should live in the MIDI module namespace");

    var visualizationDx12Readme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Visualization", "Dx12", "README.md")));
    var direct3D12AudioGraphHost = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Visualization", "Dx12", "Direct3D12AudioGraphHost.cs")));
    Assert(visualizationDx12Readme.Contains("Direct3D12AudioGraphHost.cs", StringComparison.Ordinal), "Visualization DX12 docs should name migrated audio graph host ownership");
    Assert(direct3D12AudioGraphHost.Contains("namespace JerichoDown.Modules.Visualization.Dx12;", StringComparison.Ordinal), "DX12 audio graph host should live in the Visualization DX12 module namespace");

    var mediaFoundationReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "README.md")));
    var mediaFoundationCameraEnumerator = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationCameraEnumerator.cs")));
    var mediaFoundationCameraModeService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationCameraModeService.cs")));
    var mediaFoundationCameraDeviceFactory = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationCameraDeviceFactory.cs")));
    var mediaFoundationCameraPreviewService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationCameraPreviewService.cs")));
    var mediaFoundationVideoRecorder = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationVideoRecorder.cs")));
    var mediaFoundationGuids = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationGuids.cs")));
    var mediaFoundationInterop = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationInterop.cs")));
    Assert(mediaFoundationReadme.Contains("MediaFoundationCameraEnumerator.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated camera enumerator ownership");
    Assert(mediaFoundationReadme.Contains("MediaFoundationCameraModeService.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated camera mode service ownership");
    Assert(mediaFoundationReadme.Contains("MediaFoundationCameraDeviceFactory.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated camera device factory ownership");
    Assert(mediaFoundationReadme.Contains("MediaFoundationCameraPreviewService.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated camera preview service ownership");
    Assert(mediaFoundationReadme.Contains("MediaFoundationVideoRecorder.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated video recorder ownership");
    Assert(mediaFoundationReadme.Contains("MediaFoundationGuids.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated GUID ownership");
    Assert(mediaFoundationReadme.Contains("MediaFoundationInterop.cs", StringComparison.Ordinal), "Media Foundation docs should name migrated interop ownership");
    Assert(mediaFoundationCameraEnumerator.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation camera enumerator should live in the MediaFoundation module namespace");
    Assert(mediaFoundationCameraModeService.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation camera mode service should live in the MediaFoundation module namespace");
    Assert(mediaFoundationCameraDeviceFactory.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation camera device factory should live in the MediaFoundation module namespace");
    Assert(mediaFoundationCameraPreviewService.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation camera preview service should live in the MediaFoundation module namespace");
    Assert(mediaFoundationCameraPreviewService.Contains("using JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "Media Foundation camera preview service should document its DX12 preview upload dependency");
    Assert(mediaFoundationVideoRecorder.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation video recorder should live in the MediaFoundation module namespace");
    Assert(mediaFoundationGuids.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation GUIDs should live in the MediaFoundation module namespace");
    Assert(mediaFoundationInterop.Contains("namespace JerichoDown.Modules.Webcam.MediaFoundation;", StringComparison.Ordinal), "Media Foundation interop should live in the MediaFoundation module namespace");

    var directShowReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "DirectShow", "README.md")));
    var directShowCameraEnumerator = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "DirectShow", "DirectShowCameraEnumerator.cs")));
    var directShowCameraControlService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "DirectShow", "DirectShowCameraControlService.cs")));
    var directShowCameraPreviewService = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "DirectShow", "DirectShowCameraPreviewService.cs")));
    Assert(directShowReadme.Contains("DirectShowCameraEnumerator.cs", StringComparison.Ordinal), "DirectShow docs should name migrated camera enumerator ownership");
    Assert(directShowReadme.Contains("DirectShowCameraControlService.cs", StringComparison.Ordinal), "DirectShow docs should name migrated camera control ownership");
    Assert(directShowReadme.Contains("DirectShowCameraPreviewService.cs", StringComparison.Ordinal), "DirectShow docs should name migrated camera preview ownership");
    Assert(directShowCameraEnumerator.Contains("namespace JerichoDown.Modules.Webcam.DirectShow;", StringComparison.Ordinal), "DirectShow camera enumerator should live in the DirectShow module namespace");
    Assert(directShowCameraControlService.Contains("namespace JerichoDown.Modules.Webcam.DirectShow;", StringComparison.Ordinal), "DirectShow camera control service should live in the DirectShow module namespace");
    Assert(directShowCameraPreviewService.Contains("namespace JerichoDown.Modules.Webcam.DirectShow;", StringComparison.Ordinal), "DirectShow camera preview service should live in the DirectShow module namespace");

    var dx12Readme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "README.md")));
    var direct3D12DeviceManager = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "Direct3D12DeviceManager.cs")));
    var direct3D12PreviewHost = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "Direct3D12PreviewHost.cs")));
    var dx12Camera = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "Dx12Camera.cs")));
    var dx12CameraOptions = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "Dx12CameraOptions.cs")));
    var cameraPreviewFramePumps = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "CameraPreviewFramePumps.cs")));
    var textureNativeCameraRecorder = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "TextureNativeCameraRecorder.cs")));
    var textureNativeCameraProbe = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "TextureNativeCameraProbe.cs")));
    Assert(dx12Readme.Contains("Direct3D12DeviceManager.cs", StringComparison.Ordinal), "DX12 docs should name migrated device manager ownership");
    Assert(dx12Readme.Contains("Direct3D12PreviewHost.cs", StringComparison.Ordinal), "DX12 docs should name migrated preview host ownership");
    Assert(dx12Readme.Contains("Dx12Camera.cs", StringComparison.Ordinal), "DX12 docs should name migrated camera ownership");
    Assert(dx12Readme.Contains("Dx12CameraOptions.cs", StringComparison.Ordinal), "DX12 docs should name migrated camera options ownership");
    Assert(dx12Readme.Contains("CameraPreviewFramePumps.cs", StringComparison.Ordinal), "DX12 docs should name migrated frame pump ownership");
    Assert(dx12Readme.Contains("TextureNativeCameraRecorder.cs", StringComparison.Ordinal), "DX12 docs should name migrated texture-native recorder ownership");
    Assert(dx12Readme.Contains("TextureNativeCameraProbe.cs", StringComparison.Ordinal), "DX12 docs should name migrated texture-native probe ownership");
    Assert(direct3D12DeviceManager.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "D3D12 device manager should live in the DX12 module namespace");
    Assert(direct3D12DeviceManager.Contains("interface ITextureNativeDeviceManager", StringComparison.Ordinal), "D3D12 device manager should own the texture-native device-manager abstraction");
    Assert(direct3D12PreviewHost.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "D3D12 preview host should live in the DX12 module namespace");
    Assert(dx12Camera.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "DX12 camera should live in the DX12 module namespace");
    Assert(dx12CameraOptions.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "DX12 camera options should live in the DX12 module namespace");
    Assert(cameraPreviewFramePumps.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "camera preview frame pumps should live in the DX12 module namespace");
    Assert(textureNativeCameraRecorder.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "texture-native recorder should live in the DX12 module namespace");
    Assert(textureNativeCameraProbe.Contains("namespace JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "texture-native probe should live in the DX12 module namespace");

    var dx11BridgeReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx11Bridge", "README.md")));
    var direct3D11DeviceManager = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx11Bridge", "Direct3D11DeviceManager.cs")));
    var direct3D11SharedTextureBridge = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx11Bridge", "Direct3D11SharedTextureBridge.cs")));
    Assert(dx11BridgeReadme.Contains("Direct3D11DeviceManager.cs", StringComparison.Ordinal), "DX11 bridge docs should name migrated device manager ownership");
    Assert(dx11BridgeReadme.Contains("Direct3D11SharedTextureBridge.cs", StringComparison.Ordinal), "DX11 bridge docs should name migrated shared texture bridge ownership");
    Assert(dx11BridgeReadme.Contains("Webcam/Dx12", StringComparison.Ordinal), "DX11 bridge docs should name its DX12 device-manager dependency");
    Assert(direct3D11DeviceManager.Contains("namespace JerichoDown.Modules.Webcam.Dx11Bridge;", StringComparison.Ordinal), "D3D11 device manager should live in the DX11 bridge module namespace");
    Assert(direct3D11DeviceManager.Contains("using JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "D3D11 device manager should document its texture-native interface dependency");
    Assert(direct3D11SharedTextureBridge.Contains("namespace JerichoDown.Modules.Webcam.Dx11Bridge;", StringComparison.Ordinal), "D3D11 shared texture bridge should live in the DX11 bridge module namespace");

    var webcamReadme = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "README.md")));
    var cameraDevice = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraDevice.cs")));
    var cameraDeviceCatalog = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraDeviceCatalog.cs")));
    var cameraVideoMode = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraVideoMode.cs")));
    var cameraFrame = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraFrame.cs")));
    var cameraControlKind = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraControlKind.cs")));
    var cameraControlItem = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraControlItem.cs")));
    var cameraControlText = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraControlText.cs")));
    var cameraSourceSelection = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraSourceSelection.cs")));
    var cameraProfile = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraProfile.cs")));
    var cameraProfileStore = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraProfileStore.cs")));
    var cameraStatusText = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "CameraStatusText.cs")));
    var textureNativePreviewPolicy = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "TextureNativePreviewPolicy.cs")));
    var videoRecordingPolicy = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "VideoRecordingPolicy.cs")));
    var videoFrameDenoiser = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "VideoFrameDenoiser.cs")));
    Assert(webcamReadme.Contains("CameraDevice.cs", StringComparison.Ordinal), "webcam docs should name migrated camera device ownership");
    Assert(webcamReadme.Contains("CameraDeviceCatalog.cs", StringComparison.Ordinal), "webcam docs should name migrated camera catalog ownership");
    Assert(webcamReadme.Contains("CameraVideoMode.cs", StringComparison.Ordinal), "webcam docs should name migrated camera mode ownership");
    Assert(webcamReadme.Contains("CameraFrame.cs", StringComparison.Ordinal), "webcam docs should name migrated camera frame ownership");
    Assert(webcamReadme.Contains("CameraControlKind.cs", StringComparison.Ordinal), "webcam docs should name migrated camera control kind ownership");
    Assert(webcamReadme.Contains("CameraControlItem.cs", StringComparison.Ordinal), "webcam docs should name migrated camera control item ownership");
    Assert(webcamReadme.Contains("CameraControlText.cs", StringComparison.Ordinal), "webcam docs should name migrated camera control text ownership");
    Assert(webcamReadme.Contains("CameraSourceSelection.cs", StringComparison.Ordinal), "webcam docs should name migrated camera source selection ownership");
    Assert(webcamReadme.Contains("CameraProfile.cs", StringComparison.Ordinal), "webcam docs should name migrated camera profile ownership");
    Assert(webcamReadme.Contains("CameraProfileStore.cs", StringComparison.Ordinal), "webcam docs should name migrated camera profile store ownership");
    Assert(webcamReadme.Contains("CameraStatusText.cs", StringComparison.Ordinal), "webcam docs should name migrated camera status ownership");
    Assert(webcamReadme.Contains("TextureNativePreviewPolicy.cs", StringComparison.Ordinal), "webcam docs should name migrated texture-native preview policy ownership");
    Assert(webcamReadme.Contains("VideoRecordingPolicy.cs", StringComparison.Ordinal), "webcam docs should name migrated recording policy ownership");
    Assert(webcamReadme.Contains("VideoFrameDenoiser.cs", StringComparison.Ordinal), "webcam docs should name migrated frame denoiser ownership");
    Assert(webcamReadme.Contains("VideoFrameColorSettings.cs", StringComparison.Ordinal), "webcam docs should name migrated color settings ownership");
    Assert(cameraDevice.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera device should live in the Webcam module namespace");
    Assert(cameraDeviceCatalog.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera device catalog should live in the Webcam module namespace");
    Assert(cameraVideoMode.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera video mode should live in the Webcam module namespace");
    Assert(cameraFrame.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera frame should live in the Webcam module namespace");
    Assert(cameraControlKind.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera control kind should live in the Webcam module namespace");
    Assert(cameraControlItem.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera control item should live in the Webcam module namespace");
    Assert(cameraControlText.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera control text should live in the Webcam module namespace");
    Assert(cameraSourceSelection.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera source selection should live in the Webcam module namespace");
    Assert(cameraSourceSelection.Contains("using JerichoDown.Modules.Webcam.Dx12;", StringComparison.Ordinal), "camera source selection should document its DX12 camera dependency");
    Assert(cameraProfile.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera profile should live in the Webcam module namespace");
    Assert(cameraProfileStore.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera profile store should live in the Webcam module namespace");
    Assert(cameraStatusText.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "camera status text should live in the Webcam module namespace");
    Assert(textureNativePreviewPolicy.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "texture-native preview policy should live in the Webcam module namespace");
    Assert(videoRecordingPolicy.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "video recording policy should live in the Webcam module namespace");
    Assert(videoFrameDenoiser.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "video frame denoiser should live in the Webcam module namespace");
    var videoFrameColorSettings = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "VideoFrameColorSettings.cs")));
    Assert(videoFrameColorSettings.Contains("namespace JerichoDown.Modules.Webcam;", StringComparison.Ordinal), "video frame color settings should live in the Webcam module namespace");
}

static void CameraDenoiseStaysOnDx12PreviewPaths()
{
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var mediaFoundationPreview = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationCameraPreviewService.cs")));
    var directShowPreview = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "DirectShow", "DirectShowCameraPreviewService.cs")));
    var dx12Preview = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "Direct3D12PreviewHost.cs")));

    Assert(windowCode.Contains("DenoiseHandledByPreviewRenderer = denoiseHandledByPreviewRenderer || _dx12Camera?.IsReady == true", StringComparison.Ordinal), "CPU preview services should know when the DX12 preview renderer owns denoise");
    Assert(windowCode.Contains("denoiseHandledByPreviewRenderer: denoiseEnabled", StringComparison.Ordinal), "Media Foundation denoise startup should avoid capture-thread denoise when DX12 will render the preview");
    Assert(windowCode.Contains("denoiseHandledByPreviewRenderer: _pendingVideoDenoiseEnabled", StringComparison.Ordinal), "DirectShow fallback denoise startup should avoid capture-thread denoise when DX12 will render the preview");
    Assert(mediaFoundationPreview.Contains("DenoiseEnabled && !DenoiseHandledByPreviewRenderer", StringComparison.Ordinal), "Media Foundation preview should not denoise BGRA frames on CPU when DX12 owns denoise");
    Assert(mediaFoundationPreview.Contains("if (DenoiseEnabled && !DenoiseHandledByPreviewRenderer)", StringComparison.Ordinal), "Media Foundation preview should only force BGRA conversion for CPU denoise");
    Assert(directShowPreview.Contains("DenoiseHandledByPreviewRenderer", StringComparison.Ordinal), "DirectShow preview should expose the same renderer-owned denoise switch");
    Assert(directShowPreview.Contains("DenoiseEnabled && !DenoiseHandledByPreviewRenderer", StringComparison.Ordinal), "DirectShow preview should not denoise on CPU when DX12 owns denoise");
    Assert(dx12Preview.Contains("ApplyBgraDenoise", StringComparison.Ordinal), "DX12 BGRA preview shader should perform GPU denoise for non-NV12 fallback frames");
    Assert(dx12Preview.Contains("SetNv12DenoiseConstants(width, height, denoiseEnabled, denoiseStrength)", StringComparison.Ordinal), "DX12 NV12 preview shader should keep the existing GPU denoise path");
}

static void MidiTabIsOptInAndOrderedAfterKaraoke()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var stateCode = File.ReadAllText(FindRepoFile("AppStateStore.cs"));

    Assert(xaml.Contains("x:Name=\"MidiTabItem\" Header=\"MIDI\" Visibility=\"Collapsed\"", StringComparison.Ordinal), "MIDI tab should be hidden by default");
    Assert(xaml.Contains("x:Name=\"RefreshMidiDevicesMenuItem\"", StringComparison.Ordinal), "MIDI refresh menu item should be addressable");
    Assert(xaml.Contains("x:Name=\"MidiHelpMenuItem\"", StringComparison.Ordinal), "MIDI help menu item should be addressable");

    var orderMethod = ExtractSourceBetween(
        windowCode,
        "    private void OrderMainTabs()",
        "    public ObservableCollection<EqualizerBand> Bands");
    var karaokeIndex = orderMethod.IndexOf("\"Karaoke\"", StringComparison.Ordinal);
    var midiIndex = orderMethod.IndexOf("\"MIDI\"", StringComparison.Ordinal);
    Assert(karaokeIndex >= 0 && midiIndex > karaokeIndex, "MIDI tab should be ordered after Karaoke when enabled");

    Assert(windowCode.Contains("_midiEnabled = _appSettings.MidiEnabled;", StringComparison.Ordinal), "MIDI enabled state should restore from app settings");
    Assert(windowCode.Contains("ApplyMidiEnabledState(refreshDevices: _midiEnabled", StringComparison.Ordinal), "MIDI enable state should drive tab visibility and refresh behavior");
    Assert(windowCode.Contains("MidiTabItem.Visibility = _midiEnabled ? Visibility.Visible : Visibility.Collapsed;", StringComparison.Ordinal), "MIDI tab visibility should follow the File menu toggle");
    Assert(windowCode.Contains("RefreshMidiDevicesMenuItem.IsEnabled = _midiEnabled;", StringComparison.Ordinal), "MIDI refresh command should follow the File menu toggle");
    Assert(windowCode.Contains("MidiHelpMenuItem.IsEnabled = _midiEnabled;", StringComparison.Ordinal), "MIDI help command should follow the File menu toggle");
    Assert(windowCode.Contains("MidiEnabled = _midiEnabled,", StringComparison.Ordinal), "MIDI enabled state should be saved");
    Assert(stateCode.Contains("public bool MidiEnabled { get; set; }", StringComparison.Ordinal), "app settings should persist the MIDI enabled state");
}

static void VoiceProcessorUsesEveryDspSetting()
{
    var processor = string.Join(
        Environment.NewLine,
        File.ReadAllText(FindRepoFile(Path.Combine("Audio", "VoiceSampleProcessor.cs"))),
        File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioBiQuadFilterRack.cs"))),
        File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioPitchShiftProcessor.cs"))),
        File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioImpulseConvolutionProcessor.cs"))),
        File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioEnvelopeGeneratorProcessor.cs"))),
        File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioDmoEffectChain.cs"))));
    var missing = GetVoiceProcessorDspSettingProperties()
        .Where(property => !processor.Contains($".{property.Name}", StringComparison.Ordinal))
        .Select(property => property.Name)
        .ToArray();

    Assert(missing.Length == 0, $"Voice processor is missing DSP setting usage: {string.Join(", ", missing)}");
}

static IEnumerable<PropertyInfo> GetVoiceProcessorDspSettingProperties()
{
    return typeof(VoiceProcessorSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead
            && property.CanWrite
            && (property.PropertyType == typeof(bool) || property.PropertyType == typeof(double)));
}

static bool HasDirectBinding(string xaml, string propertyName)
{
    var escapedPropertyName = Regex.Escape(propertyName);
    return Regex.IsMatch(
        xaml,
        $@"\{{Binding\s+(?:Path\s*=\s*)?{escapedPropertyName}(?:\s|,|\}})",
        RegexOptions.CultureInvariant);
}

static void AudioDeviceFormatDisplayText()
{
    var format = new AudioDeviceFormat(48_000, 2, 24);

    Assert(format.ToString() == "48 kHz, 2 ch, 24-bit", "audio format display text changed unexpectedly");
}

static void AudioDeviceDiagnosticsNamesSelectedDeviceRisks()
{
    var appInput = AudioInputDevice.CreateProcessLoopback(4242, "MusicApp");
    var output = new AudioOutputDevice(-1, "Default playback device");
    var report = AudioDeviceDiagnostics.BuildReport(
        appInput,
        output,
        new AudioDeviceFormat(48_000, 2, 32),
        new AudioDeviceFormat(44_100, 2, 16),
        [appInput],
        [output]);

    Assert(report.Contains("Audio Device Diagnostics", StringComparison.Ordinal), "diagnostics report should have a clear title");
    Assert(report.Contains("Selected Input", StringComparison.Ordinal), "diagnostics report should identify the selected input");
    Assert(report.Contains("MusicApp", StringComparison.Ordinal), "diagnostics report should include the selected app audio input");
    Assert(report.Contains("Process loopback target PID: 4242", StringComparison.Ordinal), "diagnostics report should show app-loopback process IDs");
    Assert(report.Contains("sample rates differ", StringComparison.OrdinalIgnoreCase), "diagnostics report should warn when output resampling will be used");
    Assert(report.Contains("refresh audio devices", StringComparison.OrdinalIgnoreCase), "diagnostics report should explain app-loopback refresh behavior");

    var asioEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Focusrite USB ASIO");
    var asioInput = new AudioInputDevice(
        AudioInputDevice.AsioInputDeviceNumber,
        "ASIO: Focusrite USB ASIO",
        10,
        asioEndpoint,
        AudioInputBackend.Asio);
    var asioDiagnostics = new AsioInputCaptureDiagnostics(
        "Focusrite USB ASIO",
        48_000,
        10,
        0,
        10,
        2,
        10,
        2,
        true,
        "STA",
        true,
        37,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        0,
        null);
    var asioReport = AudioDeviceDiagnostics.BuildReport(
        asioInput,
        output,
        new AudioDeviceFormat(48_000, 10, 32),
        new AudioDeviceFormat(48_000, 2, 32),
        [asioInput],
        [output],
        asioDiagnostics,
        "48 kHz, 10 ch, 32-bit float via ASIO input Focusrite USB ASIO");
    Assert(asioReport.Contains("ASIO Runtime Diagnostics", StringComparison.Ordinal), "diagnostics report should include ASIO runtime details when a live ASIO capture exists");
    Assert(asioReport.Contains("Audio callbacks received: 0", StringComparison.Ordinal), "ASIO diagnostics should report callback count");
    Assert(asioReport.Contains("Silent output clock: 2 channel", StringComparison.Ordinal), "ASIO diagnostics should report the silent output clock path");
    Assert(asioReport.Contains("No-callback diagnosis", StringComparison.Ordinal), "ASIO diagnostics should explain the no-callback failure mode");
    Assert(asioReport.Contains("Focusrite Control", StringComparison.Ordinal), "ASIO diagnostics should name Focusrite routing/clock checks");
    Assert(asioReport.Contains("stopped automatic ASIO retries", StringComparison.Ordinal), "ASIO diagnostics should explain that automatic driver reopen loops are stopped");

    var diagnosticsSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "AudioDeviceDiagnostics.cs")));
    Assert(diagnosticsSource.Contains("AudioEndpointVolume", StringComparison.Ordinal), "diagnostics should inspect Windows endpoint volume/mute state");
    Assert(diagnosticsSource.Contains("AudioMeterInformation", StringComparison.Ordinal), "diagnostics should inspect current endpoint meter state");

    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowSource.Contains("AudioDeviceDiagnostics.BuildReport", StringComparison.Ordinal), "diagnostics menu should build a device report");
    Assert(windowSource.Contains("ShowAudioDeviceDiagnosticsDialog", StringComparison.Ordinal), "diagnostics menu should open a popup window");
    Assert(windowSource.Contains("ShowAsioNoCallbackDiagnosticsOnce(selectedDevice)", StringComparison.Ordinal), "ASIO no-callback failures should open the diagnostic report once");
    Assert(windowSource.Contains("_asioNoCallbackAutoStartSuppressedDeviceKey", StringComparison.Ordinal), "ASIO no-callback failures should suppress automatic reopen loops for the same driver");
    Assert(windowSource.Contains("CreateAsioNoCallbackSuppressedStatus()", StringComparison.Ordinal), "ASIO no-callback suppression should give the user a clear stable status");
    Assert(!windowSource.Contains("RotateAsioInputSampleRateAfterNoCallbacks", StringComparison.Ordinal), "ASIO no-callback failures should not rotate sample rates automatically");

    var loadMethod = ExtractSourceBetween(
        windowSource,
        "    private void WindowLoaded(object sender, RoutedEventArgs e)",
        "    private bool HasPersistedAppState()");
    Assert(loadMethod.Contains("_isCameraEnabled = false;", StringComparison.Ordinal), "camera preview should not auto-start from persisted state on app launch");
    Assert(!loadMethod.Contains("_isCameraEnabled = hasPersistedState && _appSettings.CameraEnabled", StringComparison.Ordinal), "persisted camera state should not power the webcam during startup");
}

static void AudioStreamRestartFailuresBackOff()
{
    var baseBackoff = GetPrivateStaticValue<TimeSpan>(typeof(EqualizerWindow), "AudioStreamRestartBaseBackoff");
    var maximumBackoff = GetPrivateStaticValue<TimeSpan>(typeof(EqualizerWindow), "AudioStreamRestartMaximumBackoff");
    Assert(baseBackoff >= TimeSpan.FromSeconds(5), "audio restart retries should not hammer the driver immediately after a failure");
    Assert(maximumBackoff >= baseBackoff && maximumBackoff <= TimeSpan.FromSeconds(60), "audio restart backoff should be bounded and user-visible");

    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowSource.Contains("_nextAudioStreamRestartAttemptUtc", StringComparison.Ordinal), "window should track the next allowed automatic audio restart");
    Assert(windowSource.Contains("_audioStreamRestartFailureCount", StringComparison.Ordinal), "window should track repeated audio restart failures");
    Assert(windowSource.Contains("Auto-retry in", StringComparison.Ordinal), "failed audio restarts should tell the user when the app will retry");
    Assert(windowSource.Contains("\"audio-stream-refresh-failed\"", StringComparison.Ordinal), "failed audio restarts should be logged for diagnostics");

    var timerMethod = ExtractSourceBetween(
        windowSource,
        "    private async void AudioDeviceFormatTimerTick",
        "    private AudioDeviceFormat? GetSelectedDeviceFormat");
    var backoffIndex = timerMethod.IndexOf("IsAudioStreamRestartBackoffActive()", StringComparison.Ordinal);
    var stoppedRestartIndex = timerMethod.IndexOf("RestartSelectedAudioStreamAsync(\"Audio stream stopped", StringComparison.Ordinal);
    Assert(backoffIndex >= 0 && stoppedRestartIndex > backoffIndex, "format timer should honor restart backoff before reopening a stopped stream");

    var startMethod = ExtractSourceBetween(
        windowSource,
        "    private void StartSelectedDevice()",
        "    private void SpectrumServiceStreamStatusChanged");
    Assert(startMethod.Contains("ResetAudioStreamRestartBackoff();", StringComparison.Ordinal), "successful manual mic start should clear restart backoff");
    Assert(startMethod.Contains("RegisterAudioStreamRestartFailure(ex, \"Mic unavailable\")", StringComparison.Ordinal), "manual mic start failures should enter the same bounded retry path");

    var restartMethod = ExtractSourceBetween(
        windowSource,
        "    private async Task RestartSelectedAudioStreamAsync",
        "    private void SpectrumAvailable");
    Assert(restartMethod.Contains("ResetAudioStreamRestartBackoff();", StringComparison.Ordinal), "successful automatic restart should clear restart backoff");
    Assert(restartMethod.Contains("RegisterAudioStreamRestartFailure(ex);", StringComparison.Ordinal), "failed automatic restart should register a bounded retry instead of looping every timer tick");
}

static void ProcessedMonitorUsesStabilityFirstBuffering()
{
    var wasapiLatency = GetPrivateStaticValue<int>(typeof(MicrophoneSpectrumService), "WasapiProcessedOutputLatencyMilliseconds");
    var waveOutLatency = GetPrivateStaticValue<int>(typeof(MicrophoneSpectrumService), "WaveOutProcessedOutputLatencyMilliseconds");
    var providerBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "ProcessedOutputBufferDuration");
    var initialBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "InitialLiveOutputBufferedDuration");
    var targetBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "TargetLiveOutputBufferedDuration");
    var maximumBuffer = GetPrivateStaticValue<TimeSpan>(typeof(MicrophoneSpectrumService), "MaximumLiveOutputBufferedDuration");

    Assert(wasapiLatency >= 100 && wasapiLatency <= 180, "WASAPI monitor latency should favor reliability over minimum delay");
    Assert(waveOutLatency >= 120 && waveOutLatency <= 220, "WaveOut fallback latency should give physical devices room to stay smooth");
    Assert(initialBuffer >= TimeSpan.FromMilliseconds(100), "initial live monitor buffer should absorb startup jitter");
    Assert(targetBuffer >= TimeSpan.FromMilliseconds(100), "target live monitor buffer should absorb callback jitter");
    Assert(maximumBuffer >= TimeSpan.FromMilliseconds(300), "maximum live monitor buffer should avoid over-trimming on slower computers");
    Assert(providerBuffer >= maximumBuffer, "processed output provider must be able to hold the stability buffer");
}

static void ProcessedOutputRoutingPrefersWasapiBeforeWaveOut()
{
    var fullOrder = ProcessedOutputRoutePlanner.CreateAttemptOrder(canUseWaveOutFallback: true);
    var expectedFullOrder = new[]
    {
        ProcessedOutputRouteBackend.WasapiFloat,
        ProcessedOutputRouteBackend.WasapiPcm,
        ProcessedOutputRouteBackend.WaveOutFloat,
        ProcessedOutputRouteBackend.WaveOutPcm,
        ProcessedOutputRouteBackend.DirectSoundFloat,
        ProcessedOutputRouteBackend.DirectSoundPcm
    };
    Assert(
        fullOrder.SequenceEqual(expectedFullOrder),
        "default output routes should try stable WASAPI, then WaveOut, then DirectSound as the last-resort fallback");

    var endpointOnlyOrder = ProcessedOutputRoutePlanner.CreateAttemptOrder(canUseWaveOutFallback: false);
    var expectedEndpointOnlyOrder = new[]
    {
        ProcessedOutputRouteBackend.WasapiFloat,
        ProcessedOutputRouteBackend.WasapiPcm
    };
    Assert(
        endpointOnlyOrder.SequenceEqual(expectedEndpointOnlyOrder),
        "endpoint routes without a matching WaveOut device should still try both WASAPI formats");
}

static void AsioOutputRoutingIsOptIn()
{
    var asioOrder = ProcessedOutputRoutePlanner.CreateAttemptOrder(canUseWaveOutFallback: false, useAsioOutput: true);
    var expectedAsioOrder = new[]
    {
        ProcessedOutputRouteBackend.AsioFloat,
        ProcessedOutputRouteBackend.AsioPcm
    };
    Assert(asioOrder.SequenceEqual(expectedAsioOrder), "ASIO output should use only the explicitly selected ASIO driver route");

    var endpointId = MicrophoneSpectrumService.CreateAsioEndpointId("Interface ASIO Driver");
    Assert(MicrophoneSpectrumService.TryGetAsioDriverName(endpointId, out var driverName), "ASIO endpoint IDs should round-trip through the parser");
    Assert(driverName == "Interface ASIO Driver", "ASIO endpoint ID should preserve the driver name");

    var asioDevice = new AudioOutputDevice(-1, "ASIO: Interface ASIO Driver", endpointId, AudioOutputBackend.Asio);
    Assert(asioDevice.IsAsio, "ASIO output devices should be identifiable without inspecting display text");
}

static void AsioControlPanelRejectsNonAsioEndpoints()
{
    Assert(!MicrophoneSpectrumService.TryShowAsioControlPanel(null, out var blankStatus), "blank endpoints should not open an ASIO control panel");
    Assert(blankStatus.Contains("ASIO", StringComparison.OrdinalIgnoreCase) || blankStatus.Contains("Select", StringComparison.OrdinalIgnoreCase), "blank endpoint status should explain that an ASIO device is required");

    Assert(!MicrophoneSpectrumService.TryShowAsioControlPanel("not-asio", out var invalidStatus), "non-ASIO endpoints should not open an ASIO control panel");
    Assert(invalidStatus.Contains("ASIO", StringComparison.OrdinalIgnoreCase) || invalidStatus.Contains("Select", StringComparison.OrdinalIgnoreCase), "invalid endpoint status should explain that an ASIO device is required");
}

static void AsioSettingsMenuPrefersSelectedAndInstalledDrivers()
{
    var method = typeof(EqualizerWindow)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(candidate => candidate.Name == "ResolvePreferredAsioSettingsEndpointId" && candidate.GetParameters().Length == 4);
    Assert(method is not null, "ASIO settings menu endpoint resolver should be available");

    var selectedInputEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Selected Input ASIO");
    var selectedOutputEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Selected Output ASIO");
    var fallbackInputEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Fallback Input ASIO");
    var fallbackOutputEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Fallback Output ASIO");

    var selectedInput = new AudioInputDevice(
        AudioInputDevice.AsioInputDeviceNumber,
        "ASIO: Selected Input ASIO",
        2,
        selectedInputEndpoint,
        AudioInputBackend.Asio);
    var selectedOutput = new AudioOutputDevice(-1, "ASIO: Selected Output ASIO", selectedOutputEndpoint, AudioOutputBackend.Asio);
    var fallbackInput = new AudioInputDevice(
        AudioInputDevice.AsioInputDeviceNumber,
        "ASIO: Fallback Input ASIO",
        2,
        fallbackInputEndpoint,
        AudioInputBackend.Asio);
    var fallbackOutput = new AudioOutputDevice(-1, "ASIO: Fallback Output ASIO", fallbackOutputEndpoint, AudioOutputBackend.Asio);
    var windowsInput = new AudioInputDevice(0, "Windows Mic", 2);
    var windowsOutput = new AudioOutputDevice(0, "Windows Speakers");

    var selectedOutputResult = (string?)method!.Invoke(null, [selectedInput, selectedOutput, Array.Empty<AudioInputDevice>(), Array.Empty<AudioOutputDevice>()]);
    Assert(selectedOutputResult == selectedOutputEndpoint, "ASIO settings should prefer the selected ASIO output driver");

    var selectedInputResult = (string?)method.Invoke(null, [selectedInput, windowsOutput, Array.Empty<AudioInputDevice>(), Array.Empty<AudioOutputDevice>()]);
    Assert(selectedInputResult == selectedInputEndpoint, "ASIO settings should use the selected ASIO input driver when output is not ASIO");

    var fallbackResult = (string?)method.Invoke(null, [windowsInput, windowsOutput, new[] { fallbackInput }, new[] { fallbackOutput }]);
    Assert(fallbackResult == fallbackOutputEndpoint, "ASIO settings should fall back to installed ASIO output drivers before input drivers");

    var missingResult = (string?)method.Invoke(null, [windowsInput, windowsOutput, Array.Empty<AudioInputDevice>(), Array.Empty<AudioOutputDevice>()]);
    Assert(missingResult is null, "ASIO settings should report no endpoint when no ASIO driver is available");
}

static void AsioCallbackTestExposesDriverModes()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var probeSource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "AsioCallbackProbe.cs")));

    Assert(xaml.Contains("Header=\"ASIO Callback Test\"", StringComparison.Ordinal), "File menu should expose an ASIO callback test");
    Assert(windowCode.Contains("AsioCallbackTestMenuClicked", StringComparison.Ordinal), "ASIO callback test menu item should have a handler");
    Assert(windowCode.Contains("_spectrumService.Stop();", StringComparison.Ordinal), "ASIO callback test should stop the live ASIO stream before probing the same driver");
    Assert(windowCode.Contains("AsioCallbackProbe.BuildReport", StringComparison.Ordinal), "ASIO callback test should open a focused probe report");
    Assert(windowCode.Contains("ClearAsioNoCallbackAutoStartSuppression();", StringComparison.Ordinal), "ASIO callback test should clear no-callback suppression after probing");
    Assert(windowCode.Contains("StartSelectedDevice();", StringComparison.Ordinal), "ASIO callback test should restart the selected input after probing");
    Assert(probeSource.Contains("Output-only silent callback test", StringComparison.Ordinal), "ASIO callback probe should test output-only callback clocking");
    Assert(probeSource.Contains("Record-only input callback test", StringComparison.Ordinal), "ASIO callback probe should test NAudio record-only callbacks");
    Assert(probeSource.Contains("Full-duplex input plus silent output callback test", StringComparison.Ordinal), "ASIO callback probe should test full-duplex input with silent output");
    Assert(probeSource.Contains("AudioAvailable callbacks", StringComparison.Ordinal), "ASIO callback probe should report input callback counts");
    Assert(probeSource.Contains("Silent output reads", StringComparison.Ordinal), "ASIO callback probe should report output provider reads");
}

static void AsioInputDevicesCarryEndpointIdentity()
{
    var endpointId = MicrophoneSpectrumService.CreateAsioEndpointId("Interface ASIO Driver");
    var device = new AudioInputDevice(
        AudioInputDevice.AsioInputDeviceNumber,
        "ASIO: Interface ASIO Driver",
        8,
        endpointId,
        AudioInputBackend.Asio);

    Assert(device.IsAsio, "ASIO input devices should be identifiable without relying on display text");
    Assert(!device.IsSystemAudioLoopback, "ASIO input devices should not be treated as loopback capture");
    Assert(device.EndpointId == endpointId, "ASIO input devices should preserve their endpoint ID");
    Assert(device.MaximumInputChannels == 8, "ASIO input devices should expose interface channel options");

    var format = MicrophoneSpectrumService.TryGetInputDeviceFormat(device)
        ?? throw new InvalidOperationException("ASIO input format fallback was unavailable.");
    Assert(format.BitsPerSample == 32, "ASIO input capture should be represented as 32-bit float");
    Assert(format.Channels == 8, "ASIO input format fallback should honor the selected interface channel count");
}

static void AsioInputSelectionsRestoreByEndpoint()
{
    var method = typeof(EqualizerWindow).GetMethod(
        "ResolvePersistedMicChannelDeviceByEndpoint",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method is not null, "endpoint-aware persisted mic resolver should be available");

    var asioEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Interface ASIO Driver");
    var asioDevice = new AudioInputDevice(
        AudioInputDevice.AsioInputDeviceNumber,
        "ASIO: Interface ASIO Driver",
        8,
        asioEndpoint,
        AudioInputBackend.Asio);
    var similarlyNamedWindowsDevice = new AudioInputDevice(0, "ASIO: Interface ASIO Driver", 2);
    var devices = new[] { similarlyNamedWindowsDevice, asioDevice };

    var restored = (AudioInputDevice?)method!.Invoke(null, [devices, asioEndpoint, "ASIO: Interface ASIO Driver"]);
    Assert(restored?.IsAsio == true, "saved ASIO inputs should restore by endpoint before display name");
    Assert(restored?.EndpointId == asioEndpoint, "restored ASIO inputs should preserve the exact selected driver endpoint");

    var missing = (AudioInputDevice?)method.Invoke(null, [devices, MicrophoneSpectrumService.CreateAsioEndpointId("Missing ASIO Driver"), "ASIO: Missing ASIO Driver"]);
    Assert(missing is null, "missing ASIO driver selections should not silently fall back to a different mic");
}

static void AsioRestartPathPreservesEndpointIdentity()
{
    var restartOverload = typeof(MicrophoneSpectrumService).GetMethod(
        "RestartCapture",
        BindingFlags.Instance | BindingFlags.Public,
        [typeof(AudioInputDevice), typeof(VoiceProcessorSettings), typeof(InputChannelMode), typeof(TimeSpan)]);
    Assert(restartOverload is not null, "audio stream restart should accept a full input device so ASIO endpoint/backend identity is preserved");

    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(
        windowCode.Contains("_spectrumService.RestartCapture(selectedDevice,", StringComparison.Ordinal),
        "UI audio stream restart must pass the selected AudioInputDevice instead of only DeviceNumber; ASIO uses endpoint/backend identity");
    Assert(
        !windowCode.Contains("_spectrumService.RestartCapture(selectedDevice.DeviceNumber", StringComparison.Ordinal),
        "UI audio stream restart should not use the Windows-only DeviceNumber overload for selected devices");
}

static void AsioInputStartupAvoidsPreOpenProbe()
{
    var serviceCode = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    Assert(!serviceCode.Contains("TryGetAsioInputChannelCount", StringComparison.Ordinal), "ASIO input startup should not pre-open drivers just to count channels");

    var inputEnumeration = ExtractSourceBetween(
        serviceCode,
        "    private static void AddAsioInputDevices(List<AudioInputDevice> devices)",
        "    private static AudioDeviceFormat? TryGetAsioInputDeviceFormat(AudioInputDevice device)");
    Assert(!inputEnumeration.Contains("new AsioOut(driverName)", StringComparison.Ordinal), "ASIO input enumeration should trust registered driver names and avoid opening drivers");
    Assert(inputEnumeration.Contains("MaximumAsioInputChannels", StringComparison.Ordinal), "ASIO input enumeration should expose safe channel options without probing the driver");

    var formatMethod = ExtractSourceBetween(
        serviceCode,
        "    private static AudioDeviceFormat? TryGetAsioInputDeviceFormat(AudioInputDevice device)",
        "    private static string CreateInputDeviceKey");
    Assert(!formatMethod.Contains("new AsioOut(driverName)", StringComparison.Ordinal), "ASIO format display should not open the driver");

    var startMethod = ExtractSourceBetween(
        serviceCode,
        "    private IWaveIn StartAsioInputCapture(",
        "    private bool TryStartWasapiCapture");
    Assert(startMethod.Contains("new AsioInputCapture(", StringComparison.Ordinal), "ASIO capture should let the real capture open clamp to available channels");
    Assert(startMethod.Contains("useSilentOutputClock: false", StringComparison.Ordinal), "live ASIO capture should use the record-only input mode that the callback probe verifies");
}

static void AsioStaDispatcherPumpsWindowsMessages()
{
    var dispatcherSource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "StaThreadDispatcher.cs")));
    Assert(dispatcherSource.Contains("using System.Windows.Threading;", StringComparison.Ordinal), "ASIO STA helper should use WPF Dispatcher infrastructure");
    Assert(dispatcherSource.Contains("Dispatcher.CurrentDispatcher", StringComparison.Ordinal), "ASIO STA helper should create a dispatcher on its dedicated thread");
    Assert(dispatcherSource.Contains("new DispatcherSynchronizationContext(dispatcher)", StringComparison.Ordinal), "ASIO STA helper should install a synchronization context before creating NAudio ASIO objects");
    Assert(dispatcherSource.Contains("Dispatcher.Run();", StringComparison.Ordinal), "ASIO STA helper should pump Windows messages for hardware drivers that depend on a message loop");
    Assert(dispatcherSource.Contains("BeginInvokeShutdown", StringComparison.Ordinal), "ASIO STA helper should shut down the dispatcher cleanly");
    Assert(!dispatcherSource.Contains("BlockingCollection", StringComparison.Ordinal), "ASIO STA helper should not block the thread in a non-pumping work loop");
}

static void AsioNoCallbackStateClearsStaleGraphs()
{
    using var service = new MicrophoneSpectrumService();
    SetPrivateField<IWaveIn?>(service, "_capture", new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)));

    var now = System.Diagnostics.Stopwatch.GetTimestamp();
    SetPrivateField(service, "_audioStreamStartedTimestamp", now);
    SetPrivateField(service, "_lastAudioCallbackTimestamp", 0L);

    Assert(!service.HasReceivedAudioCallbacks, "service should expose that an opened capture has not delivered callbacks yet");
    Assert(service.IsWaitingForFirstAudioCallback(TimeSpan.FromSeconds(3)), "fresh ASIO opens should be reported as waiting for the first callback");
    Assert(!service.AreAudioCallbacksStale(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(1400)), "fresh ASIO opens should not be stale during the startup grace period");

    var oldStart = now - (long)(System.Diagnostics.Stopwatch.Frequency * 5d);
    SetPrivateField(service, "_audioStreamStartedTimestamp", oldStart);
    Assert(!service.IsWaitingForFirstAudioCallback(TimeSpan.FromSeconds(3)), "ASIO opens should stop waiting after the startup grace period");
    Assert(service.AreAudioCallbacksStale(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(1400)), "an ASIO open with no callbacks after grace should be stale");

    SetPrivateField(service, "_lastAudioCallbackTimestamp", now);
    Assert(service.HasReceivedAudioCallbacks, "service should report true after any real audio callback arrives");

    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var clearMethod = ExtractSourceBetween(
        windowCode,
        "    private void ClearLiveSpectrumDisplay()",
        "    private SpectrumFrame CreateEmptySpectrumFrame()");
    Assert(clearMethod.Contains("_latestFrame = emptyFrame;", StringComparison.Ordinal), "clearing live audio should replace the cached frame so old graphs cannot freeze in place");
    Assert(clearMethod.Contains("_silentFrameCount = 0;", StringComparison.Ordinal), "clearing live audio should reset silent-frame state while waiting for callbacks");
    Assert(clearMethod.Contains("ClearDirect3D12GraphHosts();", StringComparison.Ordinal), "clearing live audio should wipe DX12 graph history before pushing empty frames");
    Assert(clearMethod.Contains("_waveform3DGraphHost?.AcceptFrame(selectedMicFrame);", StringComparison.Ordinal), "clearing live audio should blank the 3D selected-mic graph");
    Assert(clearMethod.Contains("_podcastSpectrumWaterfallGraphHost?.AcceptFrame(emptyFrame);", StringComparison.Ordinal), "clearing live audio should blank waterfall graphs too");

    var graphHostCode = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Visualization", "Dx12", "Direct3D12AudioGraphHost.cs")));
    Assert(graphHostCode.Contains("public void ClearFrame()", StringComparison.Ordinal), "DX12 graph hosts should expose an explicit visual clear");
    Assert(graphHostCode.Contains("public void ClearHistory()", StringComparison.Ordinal), "DX12 graph renderer should clear retained waterfall and trace history");
    Assert(graphHostCode.Contains("Array.Clear(_history);", StringComparison.Ordinal), "DX12 graph clear should remove waterfall history instead of waiting for quiet frames to age it out");

    var timerMethod = ExtractSourceBetween(
        windowCode,
        "    private async void AudioDeviceFormatTimerTick",
        "    private AudioDeviceFormat? GetSelectedDeviceFormat");
    Assert(timerMethod.Contains("IsWaitingForFirstAudioCallback(AudioCallbackStartupGrace)", StringComparison.Ordinal), "format timer should report the first-callback wait separately from silence");
    Assert(timerMethod.Contains("CreateWaitingForAudioCallbacksStatus(selectedDevice)", StringComparison.Ordinal), "format timer should show callback-waiting text while ASIO opens");
    Assert(timerMethod.Contains("!StatusText.Text.Contains(\"no audio callbacks\"", StringComparison.Ordinal), "format timer should keep an explicit no-callback diagnosis visible");
    Assert(timerMethod.Contains("!_spectrumService.HasReceivedAudioCallbacks && selectedDevice.IsAsio", StringComparison.Ordinal), "stale ASIO opens should be identified as no-callback failures");
    Assert(timerMethod.Contains("_spectrumService.Stop();", StringComparison.Ordinal), "stale ASIO no-callback streams should be stopped instead of reopened repeatedly");
    Assert(timerMethod.Contains("_asioNoCallbackAutoStartSuppressedDeviceKey = CreateAsioNoCallbackSuppressionKey(selectedDevice);", StringComparison.Ordinal), "stale ASIO no-callback streams should suppress automatic restarts for the same device");
    Assert(timerMethod.Contains("CreateAsioNoCallbackSuppressedStatus()", StringComparison.Ordinal), "stale ASIO no-callback streams should show a stable stopped status");
    Assert(!timerMethod.Contains("RotateAsioInputSampleRateAfterNoCallbacks", StringComparison.Ordinal), "stale ASIO no-callback streams should not rotate sample rates automatically");

    var signalStatusMethod = ExtractSourceBetween(
        windowCode,
        "    private void UpdateSignalStatus(double peakLevel)",
        "    private void EnsureGraphGrid");
    var callbackGuardIndex = signalStatusMethod.IndexOf("!_spectrumService.HasReceivedAudioCallbacks", StringComparison.Ordinal);
    var silentMessageIndex = signalStatusMethod.IndexOf("Listening, but this input is silent", StringComparison.Ordinal);
    Assert(callbackGuardIndex >= 0 && silentMessageIndex > callbackGuardIndex, "silent-input warning should only be possible after audio callbacks arrive");
    Assert(signalStatusMethod.Contains("!StatusText.Text.Contains(\"no audio callbacks\"", StringComparison.Ordinal), "signal status should preserve a no-callback diagnosis while empty reset frames render");
}

static void AsioInputCaptureUsesRecordOnlyLiveMode()
{
    var captureSource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Audio", "Asio", "AsioInputCapture.cs")));
    var serviceSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    var diagnosticsSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "AudioDeviceDiagnostics.cs")));

    Assert(serviceSource.Contains("useSilentOutputClock: false", StringComparison.Ordinal), "live ASIO input should use record-only startup by default");
    Assert(captureSource.Contains("useSilentOutputClock = false", StringComparison.Ordinal), "ASIO input capture should default to record-only mode");
    Assert(captureSource.Contains("asio.InitRecordAndPlayback(outputClockProvider, channelCount, _requestedSampleRate)", StringComparison.Ordinal), "ASIO input capture should initialize record plus silent playback when outputs are available");
    Assert(captureSource.Contains("_useSilentOutputClock", StringComparison.Ordinal), "ASIO input capture should keep silent output clocking optional");
    Assert(captureSource.Contains("asio.DriverOutputChannelCount", StringComparison.Ordinal), "ASIO input capture should only create output buffers when the driver exposes outputs");
    Assert(captureSource.Contains("if (InputChannelOffset > 0)", StringComparison.Ordinal), "ASIO input capture should leave the driver's default zero input offset alone");
    Assert(!captureSource.Contains("PlaybackStopped += AsioPlaybackStopped", StringComparison.Ordinal), "ASIO input startup should not let playback-stopped edges abort the driver before input callbacks arrive");
    Assert(!captureSource.Contains("DriverResetRequest += AsioDriverResetRequested", StringComparison.Ordinal), "ASIO input startup should match the callback probe and avoid reset hooks before input callbacks arrive");
    Assert(diagnosticsSource.Contains("disabled (record-only input)", StringComparison.Ordinal), "ASIO diagnostics should make record-only live input mode explicit");

    var providerType = typeof(AsioInputCapture).GetNestedType("SilentAsioOutputProvider", BindingFlags.NonPublic);
    Assert(providerType is not null, "silent ASIO output provider should be nested inside the input capture wrapper");
    var provider = (IWaveProvider)Activator.CreateInstance(
        providerType!,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [48_000, 2],
        culture: null)!;

    Assert(provider.WaveFormat.SampleRate == 48_000, "silent ASIO output clock should use the requested sample rate");
    Assert(provider.WaveFormat.Channels == 2, "silent ASIO output clock should use the selected output channel count");

    var buffer = Enumerable.Repeat((byte)0x7F, 48).ToArray();
    var read = provider.Read(buffer, 8, 24);
    Assert(read == 24, "silent ASIO output provider should return a full buffer so ASIO playback never auto-stops");
    Assert(buffer.Take(8).All(value => value == 0x7F), "silent ASIO output provider should not touch bytes before the requested range");
    Assert(buffer.Skip(8).Take(24).All(value => value == 0), "silent ASIO output provider should clear the requested playback range");
    Assert(buffer.Skip(32).All(value => value == 0x7F), "silent ASIO output provider should not touch bytes after the requested range");
}

static void AsioPrimaryCaptureHoldsAuxiliaryInputs()
{
    var serviceCode = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    var startAdditionalMethod = ExtractSourceBetween(
        serviceCode,
        "    private void StartAdditionalCaptures()",
        "    private static string DescribeInputDevice");

    Assert(
        startAdditionalMethod.Contains("currentBackend != AudioInputBackend.Asio", StringComparison.Ordinal),
        "auxiliary capture startup should not open extra inputs while ASIO is the primary capture");
    Assert(
        startAdditionalMethod.Contains("channel.Backend != AudioInputBackend.Asio", StringComparison.Ordinal),
        "auxiliary capture startup should never open ASIO as a secondary client");
    Assert(
        startAdditionalMethod.Contains("_suppressedAdditionalCaptureCount", StringComparison.Ordinal),
        "held auxiliary inputs should be counted so diagnostics explain why aux streams are absent");

    var summaryMethod = ExtractSourceBetween(
        serviceCode,
        "    private string GetAdditionalCaptureSummary()",
        "    private void SetProcessedOutputEnabled");
    Assert(
        summaryMethod.Contains("aux held while ASIO is primary", StringComparison.Ordinal),
        "active stream diagnostics should say when aux inputs are held for ASIO stability");
}

static void AsioInputCaptureConvertsInterleavedFloats()
{
    var samples = new[] { 0.25f, -0.5f, 1f, -1f };
    var bytes = new byte[samples.Length * sizeof(float)];
    var byteCount = AsioInputCapture.CopyInterleavedSamplesToBytes(samples, bytes);
    var roundTrip = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, byteCount));

    Assert(byteCount == bytes.Length, "ASIO sample conversion should write one float-sized block per interleaved sample");
    Assert(roundTrip.SequenceEqual(samples), "ASIO capture conversion should preserve interleaved 32-bit float samples exactly");
}

static void CoreAudioSessionCatalogSkipsAsioOutputs()
{
    var asioDevice = new AudioOutputDevice(
        -1,
        "ASIO: Interface ASIO Driver",
        MicrophoneSpectrumService.CreateAsioEndpointId("Interface ASIO Driver"),
        AudioOutputBackend.Asio);
    var sessions = MicrophoneSpectrumService.GetOutputAudioSessions(asioDevice);
    Assert(sessions.Count == 0, "ASIO drivers should not be queried as Windows CoreAudio app sessions");
    Assert(
        !MicrophoneSpectrumService.TrySetOutputAudioSessionControls(asioDevice, "instance", "session", 0.5f, false, out var asioControlStatus),
        "ASIO drivers should not accept Windows CoreAudio app-session control updates");
    Assert(asioControlStatus.Contains("ASIO", StringComparison.OrdinalIgnoreCase), "ASIO CoreAudio control status should explain why controls are unavailable");

    Assert(
        CoreAudioSessionCatalog.CreateDisplayTitle(string.Empty, "MusicApp", 1200, isSystemSoundsSession: false) == "MusicApp",
        "session display names should fall back to process names");
    Assert(
        CoreAudioSessionCatalog.CreateDisplayTitle(string.Empty, string.Empty, 1200, isSystemSoundsSession: false) == "PID 1200",
        "session display names should fall back to process IDs");
    Assert(
        CoreAudioSessionCatalog.CreateDisplayTitle("Browser audio", "Browser", 1200, isSystemSoundsSession: true) == "System Sounds",
        "system sounds should use the stable Windows label");

    var windowsDevice = new AudioOutputDevice(-1, "Default playback device");
    var activeSession = new CoreAudioSessionSnapshot(
        string.Empty,
        "MusicApp",
        1200,
        IsSystemSoundsSession: false,
        IsMuted: false,
        Volume: 0.5f,
        PeakLevel: 0.25f,
        State: "Active",
        SessionIdentifier: "session",
        SessionInstanceIdentifier: "instance");
    var duplicateSession = new CoreAudioSessionSnapshot(
        "Music App Stream",
        "MusicApp",
        1300,
        IsSystemSoundsSession: false,
        IsMuted: false,
        Volume: 0.25f,
        PeakLevel: 0.75f,
        State: "Inactive",
        SessionIdentifier: "session-2",
        SessionInstanceIdentifier: "instance-2");
    var collapsedSessions = CoreAudioSessionCatalog.CollapseDuplicateSessions([activeSession, duplicateSession]);
    Assert(collapsedSessions.Count == 1, "duplicate visible app sessions should collapse into one widget row");
    Assert(collapsedSessions[0].SessionCount == 2, "collapsed app session should preserve the hidden session count");
    Assert(collapsedSessions[0].ControlTargets.Count == 2, "collapsed app session should keep every Windows session control target");
    Assert(collapsedSessions[0].DisplayTitle == "MusicApp", "active process identity should win over duplicate display-name noise");
    Assert(CoreAudioSessionCatalog.FormatProcessIdentity(collapsedSessions[0]).Contains("MusicApp PID 1200", StringComparison.Ordinal), "CoreAudio grouped sessions should expose app process identity");
    Assert(CoreAudioSessionCatalog.FormatSessionSummary(collapsedSessions[0]).Contains("[MusicApp PID 1200]", StringComparison.Ordinal), "CoreAudio session summaries should include process identity for duplicate rows");
    Assert(Math.Abs(collapsedSessions[0].PeakLevel - 0.75f) < 0.0001f, "collapsed app session should show the loudest duplicate peak");
    var text = (string)InvokeEqualizerWindowPrivateStaticWithArgs(
        "BuildOutputAudioSessionText",
        [windowsDevice, collapsedSessions]);
    Assert(text.Contains("MusicApp", StringComparison.Ordinal), "output panel should show active app session names");
    Assert(text.Contains("Active", StringComparison.Ordinal), "output panel should show active app session state");
    Assert(text.Contains("2 sessions", StringComparison.Ordinal), "output panel should explain when Windows exposes multiple sessions for one app");

    var asioText = (string)InvokeEqualizerWindowPrivateStaticWithArgs(
        "BuildOutputAudioSessionText",
        [asioDevice, Array.Empty<CoreAudioSessionSnapshot>()]);
    Assert(asioText.Contains("ASIO", StringComparison.Ordinal), "output panel should explain ASIO session visibility");

    var catalogSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "CoreAudioSessionCatalog.cs")));
    Assert(catalogSource.Contains("SimpleAudioVolume", StringComparison.Ordinal), "CoreAudio controls should use Windows SimpleAudioVolume");
    Assert(catalogSource.Contains("simpleVolume.Volume =", StringComparison.Ordinal), "CoreAudio controls should be able to set app-session volume");
    Assert(catalogSource.Contains("simpleVolume.Mute =", StringComparison.Ordinal), "CoreAudio controls should be able to set app-session mute");
    Assert(catalogSource.Contains("CollapseDuplicateSessions", StringComparison.Ordinal), "CoreAudio app controls should collapse duplicate Windows sessions");
    Assert(catalogSource.Contains("validTargets.Any", StringComparison.Ordinal), "CoreAudio grouped controls should apply to every hidden session target");

    var windowXaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(windowXaml.Contains("CoreAudio App Mix", StringComparison.Ordinal), "Mixing tab should expose CoreAudio app-session controls");
    Assert(windowXaml.Contains("CoreAudioSessionsItemsControl", StringComparison.Ordinal), "CoreAudio session controls should render as a controllable list");
    Assert(windowXaml.Contains("CoreAudioSessionVolumeChanged", StringComparison.Ordinal), "CoreAudio session volume sliders should be wired");
    Assert(windowXaml.Contains("CoreAudioSessionMuteChanged", StringComparison.Ordinal), "CoreAudio session mute toggles should be wired");
    Assert(windowXaml.Contains("PeakLevelPercent", StringComparison.Ordinal), "CoreAudio app-session rows should expose live activity meters");
    Assert(windowXaml.Contains("Value=\"{Binding PeakLevelPercent, Mode=OneWay}\"", StringComparison.Ordinal), "CoreAudio peak meters should bind one-way so the read-only meter property cannot crash the Mixing tab template");
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowCode.Contains("ProcessDisplayText", StringComparison.Ordinal), "CoreAudio app-session rows should include stable process identity details");
    var controlMethod = ExtractSourceBetween(
        windowCode,
        "    private void ApplyCoreAudioSessionControls(CoreAudioSessionControlItem item, float? volume, bool? isMuted)",
        "    private void UpdateAudioFormatRouteText()");
    var successStatusIndex = controlMethod.IndexOf("StatusText.Text = status;", StringComparison.Ordinal);
    var successRefreshIndex = controlMethod.IndexOf("UpdateOutputAudioSessionText();", successStatusIndex, StringComparison.Ordinal);
    var successReturnIndex = controlMethod.IndexOf("return;", successStatusIndex, StringComparison.Ordinal);
    Assert(successRefreshIndex > successStatusIndex && successRefreshIndex < successReturnIndex, "CoreAudio successful app-session control updates should refresh grouped app state immediately");

    var mixingTab = ExtractSourceBetween(
        windowXaml,
        "      <TabItem x:Name=\"MixingTabItem\" Header=\"Mixing\">",
        "      <TabItem x:Name=\"MidiTabItem\" Header=\"MIDI\" Visibility=\"Collapsed\">");
    var recordingIndex = mixingTab.IndexOf("Text=\"Recording\"", StringComparison.Ordinal);
    var coreAudioIndex = mixingTab.IndexOf("Text=\"CoreAudio App Mix\"", StringComparison.Ordinal);
    Assert(recordingIndex >= 0, "Mixing tab should include the recording section");
    Assert(coreAudioIndex > recordingIndex, "CoreAudio App Mix should sit below the recording section in the Mixing tab");
}

static void ProcessedOutputStatusReportsActualPlaybackFormat()
{
    using var service = new MicrophoneSpectrumService();
    var sourceProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(96_000, 2));
    var playbackProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2));

    SetPrivateField(service, "_activeSampleRate", 96_000);
    SetPrivateField(service, "_processedOutputEnabled", 1);
    SetPrivateField(service, "_processedOutputBackendDescription", "WASAPI shared");
    SetPrivateField(service, "_processedOutputProvider", sourceProvider);
    SetPrivateField<IWaveProvider?>(service, "_processedOutputPlaybackProvider", playbackProvider);

    Assert(
        service.ActualProcessedOutputFormatStatus.Contains("44.1 kHz", StringComparison.Ordinal),
        "actual output format should report the provider feeding playback, not only the write buffer");
    Assert(
        service.IsProcessedOutputFormatConstrained,
        "resampled playback should be shown as constrained against the active mix target");
    Assert(
        service.ProcessedOutputFormatStatus.Contains("writing 96 kHz", StringComparison.Ordinal)
            && service.ProcessedOutputFormatStatus.Contains("playback 44.1 kHz", StringComparison.Ordinal),
        "route status should show both source and playback formats when Windows resamples");
}

static void InputChannelModesMapInterfaceLanes()
{
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.Input1Left) == 0, "input 1 should map to channel index 0");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.Input2Right) == 1, "input 2 should map to channel index 1");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.Input10) == 9, "input 10 should map to channel index 9");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.MonoSum) is null, "mono sum should not pick one channel");
    Assert(InputChannelModeInfo.GetSelectedChannelIndex(InputChannelMode.StereoPair) is null, "stereo pair should not collapse to one selected channel");
    Assert(InputChannelModeInfo.GetChannelMode(0) == InputChannelMode.Input1Left, "channel index 0 should map back to input 1");
    Assert(InputChannelModeInfo.GetChannelMode(9) == InputChannelMode.Input10, "channel index 9 should map back to input 10");
    Assert(InputChannelModeInfo.GetChannelMode(10) is null, "channel index 10 should be outside the supported strip inputs");
    Assert(InputChannelModeInfo.GetDisplayLabel(InputChannelMode.StereoPair) == "Stereo pair", "stereo pair label should be stable");
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
    var coercedStereo = (InputChannelMode)method.Invoke(null, [headset, null, InputChannelMode.StereoPair])!;

    Assert(coerced == InputChannelMode.MonoSum, "a mono headset should not keep an unavailable right-channel route");
    Assert(coercedStereo == InputChannelMode.MonoSum, "a mono headset should not keep unavailable stereo-pair routing");
}

static void AudioDeviceRefreshSuppressesMicSelectionChurn()
{
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var method = ExtractSourceBetween(
        windowCode,
        "    private void RefreshAudioDevicesFromSystem()",
        "    private void RefreshVideoDevicesFromSystem()");

    var guardIndex = method.IndexOf("_isUpdatingMicChannelUi = true;", StringComparison.Ordinal);
    var itemsSourceIndex = method.IndexOf("MicrophoneComboBox.ItemsSource = equalizerInputDevices;", StringComparison.Ordinal);
    var releaseIndex = method.IndexOf("_isUpdatingMicChannelUi = false;", StringComparison.Ordinal);
    Assert(guardIndex >= 0, "audio device refresh should suppress mic selection events before replacing input device lists");
    Assert(itemsSourceIndex > guardIndex, "microphone ItemsSource replacement should happen while mic UI events are suppressed");
    Assert(releaseIndex > itemsSourceIndex, "audio device refresh should release mic UI event suppression after restoring selections");
}

static void MicDspTabExcludesLoopbackInputs()
{
    var appAudio = AudioInputDevice.CreateProcessLoopback(4242, "MusicApp");
    var systemAudio = AudioInputDevice.CreateSystemAudioLoopback();
    var testTone = AudioInputDevice.CreateStereoTestTone();
    var physical = new AudioInputDevice(0, "Interface input", 2);
    var asio = new AudioInputDevice(
        AudioInputDevice.AsioInputDeviceNumber,
        "ASIO: Interface",
        2,
        MicrophoneSpectrumService.CreateAsioEndpointId("Interface"),
        AudioInputBackend.Asio);

    var filterMethod = typeof(EqualizerWindow).GetMethod(
        "GetEqualizerInputDevices",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(filterMethod is not null, "Mic/DSP input filter should be available");
    var filtered = ((IEnumerable<AudioInputDevice>)filterMethod!.Invoke(null, [new[] { appAudio, systemAudio, testTone, physical, asio }])!).ToArray();

    Assert(filtered.Contains(physical), "Mic/DSP input picker should keep physical Windows capture inputs");
    Assert(filtered.Contains(asio), "Mic/DSP input picker should keep ASIO capture inputs");
    Assert(!filtered.Any(device => device.IsSystemAudioLoopback), "Mic/DSP input picker should hide computer audio loopback");
    Assert(!filtered.Any(device => device.IsProcessLoopback), "Mic/DSP input picker should hide app loopback inputs");
    Assert(!filtered.Any(device => device.IsStereoTestTone), "Mic/DSP input picker should hide mixer-only test tone inputs");

    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var refreshMethod = ExtractSourceBetween(
        windowCode,
        "    private void RefreshAudioDevicesFromSystem()",
        "    private void RefreshVideoDevicesFromSystem()");
    Assert(refreshMethod.Contains("MicrophoneComboBox.ItemsSource = equalizerInputDevices;", StringComparison.Ordinal), "audio refresh should bind the Mic/DSP picker to the filtered input list");
    Assert(refreshMethod.Contains("RefreshEqualizerMicChannelList();", StringComparison.Ordinal), "audio refresh should rebuild the Mic/DSP channel list without mixer-only channels");

    var mixerSelectionHelper = ExtractSourceBetween(
        windowCode,
        "    private void ApplyActiveMicChannelSelectionToMixerUi()",
        "    private void ApplySelectedMixerInputPanelToUi()");
    Assert(mixerSelectionHelper.Contains("IsEqualizerEditableMicChannel(_activeMicChannel)", StringComparison.Ordinal), "mixer-only selections should not force the Mic/DSP channel picker onto loopback");
    Assert(windowCode.Contains("EnsureEqualizerEditorChannelSelectedAsync", StringComparison.Ordinal), "entering Mic/DSP should restore an editable mic channel if mixer selected loopback");

    var micSelectionMethod = ExtractSourceBetween(
        windowCode,
        "    private async void MicrophoneSelectionChanged",
        "    private async void InputChannelSelectionChanged");
    var rejectionIndex = micSelectionMethod.IndexOf("!IsEqualizerInputDevice(_selectedDevice)", StringComparison.Ordinal);
    var assignmentIndex = micSelectionMethod.IndexOf("_activeMicChannel.SelectedDevice = _selectedDevice;", StringComparison.Ordinal);
    Assert(rejectionIndex >= 0, "Mic/DSP source changes should reject loopback defensively");
    Assert(assignmentIndex > rejectionIndex, "Mic/DSP source changes should reject loopback before assigning the active editor channel");
    Assert(micSelectionMethod.Contains("Computer audio and loopback inputs are mixer-only sources.", StringComparison.Ordinal), "Mic/DSP source changes should explain that loopback belongs on the Mixing tab");
}

static void SystemAudioLoopbackIsSelectableButNotDefaultMicFallback()
{
    var loopback = AudioInputDevice.CreateSystemAudioLoopback();
    Assert(loopback.IsSystemAudioLoopback, "system audio loopback should identify itself");
    Assert(loopback.MaximumInputChannels == 2, "system audio loopback should expose stereo channel options");

    var method = typeof(EqualizerWindow).GetMethod(
        "ResolvePersistedMicChannelDevice",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method is not null, "persisted mic device resolver should be available");

    var physical = new AudioInputDevice(0, "Interface input", 2);
    var devices = new[] { loopback, physical };
    var restoredLoopback = (AudioInputDevice?)method!.Invoke(null, [devices, AudioInputDevice.SystemAudioLoopbackDeviceName]);
    var missingPhysical = (AudioInputDevice?)method.Invoke(null, [devices, "Old unplugged mic"]);

    Assert(restoredLoopback?.IsSystemAudioLoopback == true, "saved loopback channels should restore exactly");
    Assert(missingPhysical?.DeviceNumber == physical.DeviceNumber, "missing saved mics should fall back to a physical input instead of loopback");
}

static void AppAudioLoopbackRoutesThroughMixerCapturePath()
{
    var appInput = AudioInputDevice.CreateProcessLoopback(4242, "MusicApp");
    Assert(appInput.IsProcessLoopback, "app audio loopback devices should identify themselves");
    Assert(appInput.Backend == AudioInputBackend.ProcessLoopback, "app audio loopback should use a distinct input backend");
    Assert(appInput.MaximumInputChannels == 2, "app audio loopback should expose stereo channel options");
    Assert(appInput.Name.Contains("MusicApp", StringComparison.Ordinal), "app audio loopback should include the app name in mixer input dropdowns");
    Assert(AudioInputDevice.TryGetProcessLoopbackTargetProcessId(appInput.DeviceNumber, appInput.EndpointId, out var processId), "app audio loopback should round-trip its target process ID");
    Assert(processId == 4242, "app audio loopback should preserve the selected target process ID");

    var captureSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "ProcessLoopbackCapture.cs")));
    Assert(captureSource.Contains(@"VAD\Process_Loopback", StringComparison.Ordinal), "process loopback capture should activate the Windows virtual process-loopback device");
    Assert(captureSource.Contains("ActivateAudioInterfaceAsync", StringComparison.Ordinal), "process loopback capture should use the Windows async audio interface activation API");
    Assert(captureSource.Contains("IncludeTargetProcessTree", StringComparison.Ordinal), "process loopback capture should include the selected app process tree");

    var serviceSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    Assert(serviceSource.Contains("CoreAudioSessionCatalog.GetProcessLoopbackInputDevices()", StringComparison.Ordinal), "audio input discovery should expose active app audio sessions as mixer inputs");
    Assert(serviceSource.Contains("StartProcessLoopbackCapture", StringComparison.Ordinal), "mixer capture startup should route app audio devices through process loopback");
    Assert(serviceSource.Contains("WASAPI process loopback", StringComparison.Ordinal), "stream status should name process loopback clearly");
}

static void LoopbackCapturesShutDownWithoutZombieWorkers()
{
    var captureSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "ProcessLoopbackCapture.cs")));
    Assert(captureSource.Contains("IsBackground = true", StringComparison.Ordinal), "process loopback capture should not keep the app process alive if Windows audio is slow to stop");
    Assert(captureSource.Contains("captureThread.Join(TimeSpan.FromSeconds(2))", StringComparison.Ordinal), "process loopback capture stop should use a bounded wait");
    Assert(captureSource.Contains("ReleaseAudioClient();", StringComparison.Ordinal), "process loopback capture should release Windows audio clients on stop and dispose");
    Assert(captureSource.Contains("Windows did not complete process-loopback activation", StringComparison.Ordinal), "process loopback activation should time out instead of hanging forever");

    var serviceSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    var stopMethod = ExtractSourceBetween(
        serviceSource,
        "    public void Stop()",
        "    private void RestartProcessedOutput()");
    Assert(stopMethod.Contains("_autoRecoverCapture = false;", StringComparison.Ordinal), "stopping audio should disable capture auto-recovery");
    Assert(stopMethod.Contains("StopAdditionalCaptures();", StringComparison.Ordinal), "stopping audio should dispose mixer-only loopback captures");

    var disposeMethod = ExtractSourceBetween(
        serviceSource,
        "        _isDisposing = true;",
        "    public void StartProcessedAudioRecording");
    Assert(disposeMethod.Contains("_isDisposing = true;", StringComparison.Ordinal), "disposing audio should mark the service as closing");
    Assert(disposeMethod.Contains("Stop();", StringComparison.Ordinal), "disposing audio should stop active capture paths");
    Assert(serviceSource.Contains("&& !_isDisposing", StringComparison.Ordinal), "capture recovery should not restart streams while the service is disposing");
    Assert(serviceSource.Contains("capture.Dispose();", StringComparison.Ordinal), "released capture devices should be disposed");

    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var closingMethod = ExtractSourceBetween(
        windowSource,
        "    private void WindowClosing",
        "    private static void DisposeGraphHost");
    Assert(closingMethod.Contains("_isClosing = true;", StringComparison.Ordinal), "window close should enter closing mode before tearing down audio");
    Assert(closingMethod.Contains("_spectrumService.Dispose()", StringComparison.Ordinal), "window close should dispose the audio service that owns loopback capture");
    Assert(closingMethod.Contains("_cameraServiceStopOperationVersion", StringComparison.Ordinal), "window close should invalidate pending async camera stop callbacks");
    Assert(closingMethod.Contains("StopPreviewServices();", StringComparison.Ordinal), "window close should synchronously stop CPU preview services");
    Assert(closingMethod.Contains("TryShutdownStep", StringComparison.Ordinal), "window close should keep cleaning up if one subsystem throws");

    var shutdownTimerMethod = ExtractSourceBetween(
        windowSource,
        "    private void StopShutdownTimers",
        "    private static void StopDispatcherTimer");
    Assert(shutdownTimerMethod.Contains("StopDispatcherTimer(_sessionPlaybackPositionTimer, SessionPlaybackPositionTimerTick)", StringComparison.Ordinal), "window close should detach the session playback timer");
    Assert(shutdownTimerMethod.Contains("StopDispatcherTimer(_karaokePlaybackPositionTimer, KaraokePlaybackPositionTimerTick)", StringComparison.Ordinal), "window close should detach the karaoke playback timer");

    var mediaFoundationPreviewSource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "MediaFoundation", "MediaFoundationCameraPreviewService.cs")));
    Assert(mediaFoundationPreviewSource.Contains("TryFlushSourceReader();", StringComparison.Ordinal), "Media Foundation preview stop should flush ReadSample before waiting for capture shutdown");
    Assert(mediaFoundationPreviewSource.Contains("CaptureStopTimeout", StringComparison.Ordinal), "Media Foundation preview stop should use a named bounded shutdown wait");

    var textureNativeSource = File.ReadAllText(FindRepoFile(Path.Combine("Modules", "Webcam", "Dx12", "TextureNativeCameraRecorder.cs")));
    Assert(textureNativeSource.Contains("TryFlushSourceReader();", StringComparison.Ordinal), "texture-native preview stop should flush ReadSample before waiting for capture shutdown");
    Assert(textureNativeSource.Contains("StreamStopTimeout", StringComparison.Ordinal), "texture-native preview stop should use a named bounded shutdown wait");
}

static void SystemAudioLoopbackMixerStripIsLeftOfMics()
{
    var method = typeof(EqualizerWindow).GetMethod(
        "CreateDefaultMicChannels",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method is not null, "default mixer channel factory should be available");

    var channels = (IList)method!.Invoke(null, [])!;
    Assert(channels.Count >= 2, "default mixer should include loopback plus mic channels");

    var first = channels[0]!;
    var second = channels[1]!;
    Assert((bool)GetProperty(first, "IsSystemAudioLoopbackChannel")!, "computer audio strip should render before mic strips");
    Assert((string)GetProperty(first, "DisplayName")! == "Computer Audio", "computer audio strip should have a clear label");
    Assert((bool)GetProperty(first, "IsMuted")!, "computer audio should start muted so loopback is opt-in");
    Assert((double)GetProperty(first, "InputGainDb")! == -6d, "computer audio should start with safe gain staging");
    Assert(((string)GetProperty(first, "RouteText")!).Contains("Stereo pair", StringComparison.Ordinal), "computer audio should default to stereo pair routing");
    Assert((int)GetProperty(second, "ChannelNumber")! == 1, "Mic 1 should render immediately after computer audio");

    static object? GetProperty(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)!.GetValue(target);
    }
}

static void StereoInputDspAppliesIndependentlyPerChannel()
{
    var sourceSamples = new float[1024];
    for (var i = 0; i < sourceSamples.Length; i += 2)
    {
        sourceSamples[i] = 0.5f;
        sourceSamples[i + 1] = -0.25f;
    }

    var source = new ArraySampleProvider(sourceSamples, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
    var settings = CreateTransparentVoiceSettings();
    settings.InputTrimDb = -18;
    var provider = new StereoVoiceProcessorSampleProvider(
        source,
        new VoiceSampleProcessor(settings, 48_000),
        new VoiceSampleProcessor(settings, 48_000));
    var output = new float[sourceSamples.Length];

    var read = provider.Read(output, 0, output.Length);

    Assert(read == sourceSamples.Length, "stereo DSP provider should read the source block");
    Assert(output[^2] > 0f && output[^2] < sourceSamples[^2] * 0.6f, "left stereo channel should receive DSP input trim");
    Assert(output[^1] < 0f && Math.Abs(output[^1]) < Math.Abs(sourceSamples[^1]) * 0.6f, "right stereo channel should receive DSP input trim");
    Assert(Math.Abs(output[^2] / output[^1] + 2d) < 0.2d, "stereo DSP should preserve independent left/right proportions");
}

static void SystemAudioLoopbackStereoProviderPreservesChannels()
{
    var source = new LiveStereoBlockSampleProvider(48_000);
    var volume = new VolumeSampleProvider(source) { Volume = 1f };
    var balance = new StereoBalanceSampleProvider(volume);
    var bus = new LiveProgramMixBus(48_000);
    bus.AddMicInput(balance);

    source.SetBlock([0.8f, -0.4f, 0.1f, -0.2f]);
    var output = new float[4];
    var read = bus.Read(output, 0, output.Length);

    Assert(read == output.Length, "loopback stereo source should keep the live bus full");
    AssertSequenceEqual([0.8f, -0.4f, 0.1f, -0.2f], output, "loopback stereo source should preserve left and right channels");

    balance.Balance = -0.5d;
    source.SetBlock([0.6f, 0.6f]);
    output = new float[2];
    bus.Read(output, 0, output.Length);

    Assert(Math.Abs(output[0] - 0.6f) < 0.0001f, "left balance should leave the left side intact");
    Assert(Math.Abs(output[1] - 0.3f) < 0.0001f, "left balance should reduce the right side without summing to mono");
}

static void NAudioStereoTestToneRoutesThroughMixerInputs()
{
    var testTone = AudioInputDevice.CreateStereoTestTone();
    Assert(testTone.IsStereoTestTone, "stereo test tone should identify itself as a virtual signal input");
    Assert(testTone.Backend == AudioInputBackend.SignalGenerator, "stereo test tone should use the signal-generator backend");
    Assert(testTone.MaximumInputChannels == 2, "stereo test tone should expose left/right channel routing");

    var format = MicrophoneSpectrumService.TryGetInputDeviceFormat(testTone);
    Assert(format == new AudioDeviceFormat(48_000, 2, 32), "stereo test tone should report a stable 48 kHz stereo float format");

    float[] samples = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];
    SignalGeneratorCapture.ApplyAlternatingStereoGate(samples, channelCount: 2, sampleRate: 2, startingFrame: 0);
    AssertSequenceEqual([1f, 0f, 1f, 0f, 0f, 1f, 0f, 1f], samples, "stereo test tone should alternate between left and right channels");

    var captureSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "SignalGeneratorCapture.cs")));
    Assert(captureSource.Contains("SignalGenerator", StringComparison.Ordinal), "stereo test tone should use NAudio SignalGenerator");
    Assert(captureSource.Contains("SignalGeneratorType.Sin", StringComparison.Ordinal), "stereo test tone should use a sine reference tone");

    var serviceSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    Assert(serviceSource.Contains("CreateStereoTestTone", StringComparison.Ordinal), "input device discovery should expose the stereo test tone");
    Assert(serviceSource.Contains("StartSignalGeneratorCapture", StringComparison.Ordinal), "capture startup should route the stereo test tone through the mixer");
    Assert(serviceSource.Contains("NAudio signal generator", StringComparison.Ordinal), "stream status should name the NAudio signal generator clearly");
}

static void BlankMixerChannelsRestoreWithoutFallbackInput()
{
    var method = typeof(EqualizerWindow).GetMethod(
        "ResolvePersistedMicChannelDevice",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method is not null, "persisted mic device resolver should be available");

    var devices = new[]
    {
        new AudioInputDevice(0, "Interface input", 2),
        new AudioInputDevice(1, "Headset mic", 1)
    };

    var blank = method!.Invoke(null, [devices, null]);
    var named = (AudioInputDevice?)method.Invoke(null, [devices, "Headset mic"]);
    var missing = (AudioInputDevice?)method.Invoke(null, [devices, "Old unplugged mic"]);

    Assert(blank is null, "blank saved mic channels should stay disconnected instead of joining the live bus");
    Assert(named?.DeviceNumber == 1, "saved mic names should restore their matching device");
    Assert(missing?.DeviceNumber == 0, "named saved mics can still fall back if the old device is missing");
}

static void AppSettingsRoundtripPreservesMicMixerRoutingState()
{
    var appAssembly = typeof(EqualizerWindow).Assembly;
    var settingsType = appAssembly.GetType("JerichoDown.AppSettingsState");
    var micStateType = appAssembly.GetType("JerichoDown.MicChannelSettingsState");
    var midiMappingStateType = appAssembly.GetType("JerichoDown.MidiControlMappingSettingsState");
    var bandStateType = appAssembly.GetType("JerichoDown.EqualizerBandSettingsState");
    Assert(settingsType is not null, "app settings state type should be available");
    Assert(micStateType is not null, "mic channel settings state type should be available");
    Assert(midiMappingStateType is not null, "MIDI control mapping settings state type should be available");
    Assert(bandStateType is not null, "equalizer band settings state type should be available");

    var settings = Activator.CreateInstance(settingsType!)!;
    Set(settings, "SelectedMicChannelNumber", 3);
    Set(settings, "MixerMasterVolumePercent", 88d);
    Set(settings, "MixerAutoNormalizeEnabled", false);
    Set(settings, "MixerLimiterEnabled", true);
    Set(settings, "MixerLimiterCeilingDb", -2.5d);
    Set(settings, "MixerOutputMode", MixBusOutputMode.Mono.ToString());
    Set(settings, "SystemAudioLoopbackDefaultMuteApplied", true);
    Set(settings, "OutputDeviceName", "Sanctuary mains");
    Set(settings, "OutputEndpointId", "{output-guid}");
    Set(settings, "ProcessedOutputEnabled", true);
    Set(settings, "AudioRecordingFolder", @"C:\Jericho\Recordings");
    Set(settings, "AudioRecordingSource", ProcessedRecordingSource.SelectedMicRawBackup.ToString());
    Set(settings, "MidiInputDeviceName", "Launchkey Mini");
    Set(settings, "MidiInputDeviceProductId", 101);
    Set(settings, "MidiOutputDeviceName", "Microsoft GS Wavetable Synth");
    Set(settings, "MidiOutputDeviceProductId", 202);
    Set(settings, "MidiSequenceSpeedPercent", 125d);

    var mic = Activator.CreateInstance(micStateType!)!;
    Set(mic, "ChannelNumber", 3);
    Set(mic, "DisplayName", "Scarlett mic");
    Set(mic, "MicrophoneName", "USB headset");
    Set(mic, "MicrophoneEndpointId", "{mic-endpoint}");
    Set(mic, "InputChannelMode", InputChannelMode.Input2Right.ToString());
    Set(mic, "IsMuted", true);
    Set(mic, "VolumePercent", 73d);
    Set(mic, "InputGainDb", -4.5d);
    Set(mic, "Pan", 35d);
    Set(mic, "PolarityInverted", true);
    Set(mic, "IsSoloed", true);
    Set(mic, "DelayMilliseconds", 18d);
    Set(mic, "ActivePresetName", "Warm Radio");
    Set(mic, "ActivePresetIsUserPreset", true);
    Set(mic, "PresetDescription", "Saved per-mic DSP");
    Set(mic, "AnalyzerSmoothing", 42d);

    var band = Activator.CreateInstance(bandStateType!)!;
    Set(band, "Label", "250");
    Set(band, "FrequencyHz", 250d);
    Set(band, "GainDb", 3.5d);
    Set(band, "IsEnabled", false);
    ((IList)Get(mic, "EqualizerBands")!).Add(band);

    var numberSettings = (IDictionary)Get(mic, "NumberSettings")!;
    numberSettings[nameof(VoiceProcessorSettings.InputTrimDb)] = -3d;
    numberSettings[nameof(VoiceProcessorSettings.LowPassFrequencyHz)] = 16_000d;
    var booleanSettings = (IDictionary)Get(mic, "BooleanSettings")!;
    booleanSettings[nameof(VoiceProcessorSettings.HighPassEnabled)] = true;
    booleanSettings[nameof(VoiceProcessorSettings.HumRemovalEnabled)] = true;
    ((IList)Get(settings, "MicChannels")!).Add(mic);

    var midiMapping = Activator.CreateInstance(midiMappingStateType!)!;
    Set(midiMapping, "ActionName", MidiControlMappingActions.ToggleSelectedInputMute);
    Set(midiMapping, "MessageType", "Control Change");
    Set(midiMapping, "Channel", 2);
    Set(midiMapping, "Data1", 64);
    ((IList)Get(settings, "MidiControlMappings")!).Add(midiMapping);

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

    var json = JsonSerializer.Serialize(settings, settingsType!, options);
    var restored = JsonSerializer.Deserialize(json, settingsType!, options);

    Assert(restored is not null, "settings should deserialize");
    Assert((int)Get(restored!, "SelectedMicChannelNumber")! == 3, "selected mic should survive app-state roundtrip");
    Assert((double)Get(restored!, "MixerMasterVolumePercent")! == 88d, "master volume should survive app-state roundtrip");
    Assert(!(bool)Get(restored!, "MixerAutoNormalizeEnabled")!, "normalize toggle should survive app-state roundtrip");
    Assert((bool)Get(restored!, "MixerLimiterEnabled")!, "limiter toggle should survive app-state roundtrip");
    Assert((double)Get(restored!, "MixerLimiterCeilingDb")! == -2.5d, "limiter ceiling should survive app-state roundtrip");
    Assert((string)Get(restored!, "MixerOutputMode")! == MixBusOutputMode.Mono.ToString(), "output mode should survive app-state roundtrip");
    Assert((bool)Get(restored!, "SystemAudioLoopbackDefaultMuteApplied")!, "loopback default mute migration flag should survive app-state roundtrip");
    Assert((string)Get(restored!, "OutputDeviceName")! == "Sanctuary mains", "output device name should survive app-state roundtrip");
    Assert((string)Get(restored!, "OutputEndpointId")! == "{output-guid}", "output endpoint should survive app-state roundtrip");
    Assert((bool)Get(restored!, "ProcessedOutputEnabled")!, "processed output toggle should survive app-state roundtrip");
    Assert((string)Get(restored!, "AudioRecordingFolder")! == @"C:\Jericho\Recordings", "recording folder should survive app-state roundtrip");
    Assert((string)Get(restored!, "AudioRecordingSource")! == ProcessedRecordingSource.SelectedMicRawBackup.ToString(), "recording source should survive app-state roundtrip");
    Assert((string)Get(restored!, "MidiInputDeviceName")! == "Launchkey Mini", "MIDI input device name should survive app-state roundtrip");
    Assert((int)Get(restored!, "MidiInputDeviceProductId")! == 101, "MIDI input product id should survive app-state roundtrip");
    Assert((string)Get(restored!, "MidiOutputDeviceName")! == "Microsoft GS Wavetable Synth", "MIDI output device name should survive app-state roundtrip");
    Assert((int)Get(restored!, "MidiOutputDeviceProductId")! == 202, "MIDI output product id should survive app-state roundtrip");
    Assert((double)Get(restored!, "MidiSequenceSpeedPercent")! == 125d, "MIDI sequence speed should survive app-state roundtrip");
    var restoredMidiMapping = ((IList)Get(restored!, "MidiControlMappings")!)[0]!;
    Assert((string)Get(restoredMidiMapping, "ActionName")! == MidiControlMappingActions.ToggleSelectedInputMute, "MIDI mapping action should survive app-state roundtrip");
    Assert((string)Get(restoredMidiMapping, "MessageType")! == "Control Change", "MIDI mapping message type should survive app-state roundtrip");
    Assert((int)Get(restoredMidiMapping, "Channel")! == 2, "MIDI mapping channel should survive app-state roundtrip");
    Assert((int)Get(restoredMidiMapping, "Data1")! == 64, "MIDI mapping data byte should survive app-state roundtrip");

    var restoredMic = ((IList)Get(restored!, "MicChannels")!)[0]!;
    Assert((int)Get(restoredMic, "ChannelNumber")! == 3, "mic channel number should survive app-state roundtrip");
    Assert((string)Get(restoredMic, "DisplayName")! == "Scarlett mic", "mic display name should survive app-state roundtrip");
    Assert((string)Get(restoredMic, "MicrophoneName")! == "USB headset", "mic device should survive app-state roundtrip");
    Assert((string)Get(restoredMic, "MicrophoneEndpointId")! == "{mic-endpoint}", "mic endpoint should survive app-state roundtrip");
    Assert((string)Get(restoredMic, "InputChannelMode")! == InputChannelMode.Input2Right.ToString(), "mic input mode should survive app-state roundtrip");
    Assert((bool)Get(restoredMic, "IsMuted")!, "mute should survive app-state roundtrip");
    Assert((double)Get(restoredMic, "VolumePercent")! == 73d, "mic volume should survive app-state roundtrip");
    Assert((double)Get(restoredMic, "InputGainDb")! == -4.5d, "mic input gain should survive app-state roundtrip");
    Assert((double)Get(restoredMic, "Pan")! == 35d, "mic pan should survive app-state roundtrip");
    Assert((bool)Get(restoredMic, "PolarityInverted")!, "mic polarity should survive app-state roundtrip");
    Assert((bool)Get(restoredMic, "IsSoloed")!, "solo should survive app-state roundtrip");
    Assert((double)Get(restoredMic, "DelayMilliseconds")! == 18d, "mic delay should survive app-state roundtrip");
    Assert((string)Get(restoredMic, "ActivePresetName")! == "Warm Radio", "mic preset should survive app-state roundtrip");
    Assert((bool)Get(restoredMic, "ActivePresetIsUserPreset")!, "mic user-preset flag should survive app-state roundtrip");
    Assert((string)Get(restoredMic, "PresetDescription")! == "Saved per-mic DSP", "mic preset description should survive app-state roundtrip");
    Assert((double)Get(restoredMic, "AnalyzerSmoothing")! == 42d, "mic analyzer setting should survive app-state roundtrip");
    var restoredBand = ((IList)Get(restoredMic, "EqualizerBands")!)[0]!;
    Assert((double)Get(restoredBand, "GainDb")! == 3.5d, "mic EQ gain should survive app-state roundtrip");
    Assert(!(bool)Get(restoredBand, "IsEnabled")!, "mic EQ enabled state should survive app-state roundtrip");
    Assert((double)((IDictionary)Get(restoredMic, "NumberSettings")!)[nameof(VoiceProcessorSettings.InputTrimDb)]! == -3d, "numeric DSP state should survive app-state roundtrip");
    Assert((bool)((IDictionary)Get(restoredMic, "BooleanSettings")!)[nameof(VoiceProcessorSettings.HighPassEnabled)]!, "boolean DSP state should survive app-state roundtrip");

    static object? Get(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)!.GetValue(target);
    }

    static void Set(object target, string propertyName, object? value)
    {
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);
    }
}

static void PrimaryCaptureSelectorFollowsActiveMicSource()
{
    var candidates = new[]
    {
        new PrimaryCaptureCandidate(1, 10, IsActive: false, IsMuted: false),
        new PrimaryCaptureCandidate(3, 42, IsActive: true, IsMuted: false),
        new PrimaryCaptureCandidate(5, 77, IsActive: false, IsMuted: false)
    };

    var selected = PrimaryCaptureSelector.ResolveChannelNumber(candidates, requestedDeviceNumber: 10);
    Assert(selected == 3, "active mic should become the primary capture even if the previous top-level device is still selected");
    Assert(candidates.First(candidate => candidate.ChannelNumber == selected).DeviceNumber == 42, "active mic selection should point startup at the active mic device");

    candidates =
    [
        new PrimaryCaptureCandidate(1, 10, IsActive: false, IsMuted: true),
        new PrimaryCaptureCandidate(3, 42, IsActive: false, IsMuted: false)
    ];
    selected = PrimaryCaptureSelector.ResolveChannelNumber(candidates, requestedDeviceNumber: 10);
    Assert(selected == 1, "requested device should remain primary when no active mic source is available");

    selected = PrimaryCaptureSelector.ResolveChannelNumber(candidates, requestedDeviceNumber: 999);
    Assert(selected == 3, "unmatched devices should fall back to the first unmuted configured mic");

    candidates =
    [
        new PrimaryCaptureCandidate(2, 12, IsActive: false, IsMuted: true),
        new PrimaryCaptureCandidate(4, 14, IsActive: false, IsMuted: true)
    ];
    selected = PrimaryCaptureSelector.ResolveChannelNumber(candidates, requestedDeviceNumber: null);
    Assert(selected == 2, "if every configured mic is muted, the selector should still pick a real source for the live bus");
}

static void PrimaryCaptureSelectorMatchesAsioEndpoint()
{
    var firstEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("First ASIO Driver");
    var secondEndpoint = MicrophoneSpectrumService.CreateAsioEndpointId("Second ASIO Driver");
    var candidates = new[]
    {
        new PrimaryCaptureCandidate(1, AudioInputDevice.AsioInputDeviceNumber, IsActive: false, IsMuted: false, firstEndpoint, AudioInputBackend.Asio),
        new PrimaryCaptureCandidate(2, AudioInputDevice.AsioInputDeviceNumber, IsActive: false, IsMuted: false, secondEndpoint, AudioInputBackend.Asio)
    };

    var selected = PrimaryCaptureSelector.ResolveChannelNumber(
        candidates,
        AudioInputDevice.AsioInputDeviceNumber,
        secondEndpoint,
        AudioInputBackend.Asio);

    Assert(selected == 2, "ASIO primary capture selection should match endpoint IDs when device numbers are shared");
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

static void LiveServiceBusRecordsInterfaceLeftAndRight()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        using var service = new MicrophoneSpectrumService();
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var capture = new FakeWaveIn(waveFormat);
        SetPrivateField<IWaveIn?>(service, "_capture", capture);
        SetPrivateField(service, "_currentDeviceNumber", 0);
        SetPrivateField(service, "_activeSampleRate", 48_000);
        SetPrivateField(service, "_inputChannelMode", InputChannelMode.MonoSum);
        service.ConfigureLiveMix(
            [
                new MicrophoneLiveChannelSettings(
                    1,
                    0,
                    InputChannelMode.Input1Left,
                    CreateTransparentVoiceSettings(),
                    100d,
                    0d,
                    -100d,
                    false,
                    false,
                    0d,
                    true,
                    false),
                new MicrophoneLiveChannelSettings(
                    2,
                    0,
                    InputChannelMode.Input2Right,
                    CreateTransparentVoiceSettings(),
                    100d,
                    0d,
                    100d,
                    false,
                    false,
                    0d,
                    true,
                    false)
            ],
            new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));
        service.ConfigureProcessedRecordingSource(ProcessedRecordingSource.ProgramMix, 1);

        var recordingPath = Path.Combine(folder, "program.wav");
        service.StartProcessedAudioRecording(recordingPath);
        var stereoSamples = new[] { 0.40f, 0.10f, -0.20f, -0.05f, 0.30f, 0.15f };
        var buffer = MemoryMarshal.AsBytes(stereoSamples.AsSpan()).ToArray();
        var method = typeof(MicrophoneSpectrumService).GetMethod(
            "CaptureDataAvailable",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(method is not null, "capture callback should be available for service integration test");
        method!.Invoke(service, [null, new WaveInEventArgs(buffer, buffer.Length)]);
        service.StopProcessedAudioRecording();

        using var reader = new WaveFileReader(recordingPath);
        var recorded = ReadWaveFloatSamples(reader);
        Assert(reader.WaveFormat.Channels == 2, "live program mix recording should remain stereo");
        Assert(recorded.Length >= stereoSamples.Length, "live program mix should write the captured block");
        Assert(Math.Abs(recorded[0] - 0.40f) < 0.04f, "mic 1 input 1 should reach the left program channel");
        Assert(Math.Abs(recorded[1] - 0.10f) < 0.04f, "mic 2 input 2 should reach the right program channel");
        Assert(Math.Abs(recorded[2] + 0.20f) < 0.05f, "left channel should preserve following input 1 samples");
        Assert(Math.Abs(recorded[3] + 0.05f) < 0.04f, "right channel should preserve following input 2 samples");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static void LiveServiceBusRecordsHigherInterfaceLanes()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        using var service = new MicrophoneSpectrumService();
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 4);
        var capture = new FakeWaveIn(waveFormat);
        SetPrivateField<IWaveIn?>(service, "_capture", capture);
        SetPrivateField(service, "_currentDeviceNumber", 0);
        SetPrivateField(service, "_activeSampleRate", 48_000);
        SetPrivateField(service, "_inputChannelMode", InputChannelMode.MonoSum);
        service.ConfigureLiveMix(
            [
                new MicrophoneLiveChannelSettings(
                    3,
                    0,
                    InputChannelMode.Input3,
                    CreateTransparentVoiceSettings(),
                    100d,
                    0d,
                    -100d,
                    false,
                    false,
                    0d,
                    true,
                    false),
                new MicrophoneLiveChannelSettings(
                    4,
                    0,
                    InputChannelMode.Input4,
                    CreateTransparentVoiceSettings(),
                    100d,
                    0d,
                    100d,
                    false,
                    false,
                    0d,
                    true,
                    false)
            ],
            new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));
        service.ConfigureProcessedRecordingSource(ProcessedRecordingSource.ProgramMix, 3);

        var recordingPath = Path.Combine(folder, "program.wav");
        service.StartProcessedAudioRecording(recordingPath);
        var fourLaneSamples = new[]
        {
            0.01f, 0.02f, 0.50f, 0.20f,
            -0.01f, -0.02f, -0.25f, -0.10f,
            0.03f, 0.04f, 0.40f, 0.30f
        };
        var buffer = MemoryMarshal.AsBytes(fourLaneSamples.AsSpan()).ToArray();
        var method = typeof(MicrophoneSpectrumService).GetMethod(
            "CaptureDataAvailable",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(method is not null, "capture callback should be available for higher-lane service integration test");
        method!.Invoke(service, [null, new WaveInEventArgs(buffer, buffer.Length)]);
        service.StopProcessedAudioRecording();

        using var reader = new WaveFileReader(recordingPath);
        var recorded = ReadWaveFloatSamples(reader);
        Assert(reader.WaveFormat.Channels == 2, "higher-lane program mix recording should remain stereo");
        Assert(recorded.Length >= 6, "higher-lane program mix should write the captured block");
        Assert(Math.Abs(recorded[0] - 0.50f) < 0.05f, "mic 3 input 3 should reach the left program channel");
        Assert(Math.Abs(recorded[1] - 0.20f) < 0.04f, "mic 4 input 4 should reach the right program channel");
        Assert(Math.Abs(recorded[2] + 0.25f) < 0.05f, "left channel should preserve following input 3 samples");
        Assert(Math.Abs(recorded[3] + 0.10f) < 0.04f, "right channel should preserve following input 4 samples");
        Assert(Math.Abs(recorded[4] - 0.40f) < 0.05f, "left channel should ignore unrelated interface lanes");
        Assert(Math.Abs(recorded[5] - 0.30f) < 0.04f, "right channel should ignore unrelated interface lanes");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static void LiveServiceBusRecordsSelectedMicSources()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var rawPath = Path.Combine(folder, "selected_raw.wav");
        var rawRecorded = RecordSelectedMicSourceBlock(rawPath, ProcessedRecordingSource.SelectedMicRawBackup);
        Assert(rawRecorded.Length >= 3, "selected raw backup should write the captured mic block");
        Assert(Math.Abs(rawRecorded[0] - 0.25f) < 0.04f, "selected raw backup should record mic 2 input 2 before DSP");
        Assert(Math.Abs(rawRecorded[1] + 0.10f) < 0.04f, "selected raw backup should preserve following raw input 2 samples");
        Assert(Math.Abs(rawRecorded[2] - 0.15f) < 0.04f, "selected raw backup should keep recording the selected mic lane");

        var processedPath = Path.Combine(folder, "selected_processed.wav");
        var processedRecorded = RecordSelectedMicSourceBlock(processedPath, ProcessedRecordingSource.SelectedMicProcessed);
        Assert(processedRecorded.Length >= 3, "selected processed mic should write the captured mic block");
        Assert(processedRecorded[0] < -0.18f, "selected processed mic should include pre-DSP polarity inversion");
        Assert(processedRecorded[1] > 0.06f, "selected processed mic should preserve inverted following samples");
        Assert(processedRecorded[2] < -0.08f, "selected processed mic should keep using processed selected mic samples");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static float[] RecordSelectedMicSourceBlock(string recordingPath, ProcessedRecordingSource source)
{
    using var service = new MicrophoneSpectrumService();
    var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    var capture = new FakeWaveIn(waveFormat);
    SetPrivateField<IWaveIn?>(service, "_capture", capture);
    SetPrivateField(service, "_currentDeviceNumber", 0);
    SetPrivateField(service, "_activeSampleRate", 48_000);
    SetPrivateField(service, "_inputChannelMode", InputChannelMode.MonoSum);
    service.ConfigureLiveMix(
        [
            new MicrophoneLiveChannelSettings(
                1,
                0,
                InputChannelMode.Input1Left,
                CreateTransparentVoiceSettings(),
                100d,
                0d,
                -100d,
                false,
                false,
                0d,
                true,
                false),
            new MicrophoneLiveChannelSettings(
                2,
                0,
                InputChannelMode.Input2Right,
                CreateTransparentVoiceSettings(),
                100d,
                0d,
                100d,
                true,
                false,
                0d,
                true,
                false)
        ],
        new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));
    service.ConfigureProcessedRecordingSource(source, 2);

    service.StartProcessedAudioRecording(recordingPath);
    var stereoSamples = new[] { 0.40f, 0.25f, -0.20f, -0.10f, 0.30f, 0.15f };
    var buffer = MemoryMarshal.AsBytes(stereoSamples.AsSpan()).ToArray();
    var method = typeof(MicrophoneSpectrumService).GetMethod(
        "CaptureDataAvailable",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method is not null, "capture callback should be available for selected source service integration test");
    method!.Invoke(service, [null, new WaveInEventArgs(buffer, buffer.Length)]);
    service.StopProcessedAudioRecording();

    using var reader = new WaveFileReader(recordingPath);
    Assert(reader.WaveFormat.Channels == 1, "selected mic recording sources should be mono");
    return ReadWaveFloatSamples(reader);
}

static void LiveOutputProviderReceivesProgramMix()
{
    var floatProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2))
    {
        ReadFully = false
    };
    FeedLiveOutputProvider(floatProvider);
    var floatSamples = ReadBufferedFloatSamples(floatProvider);
    AssertAudibleStereoProgram(floatSamples, "float output provider");

    var pcmProvider = new BufferedWaveProvider(new WaveFormat(48_000, 16, 2))
    {
        ReadFully = false
    };
    FeedLiveOutputProvider(pcmProvider);
    var pcmSamples = ReadBufferedPcm16Samples(pcmProvider);
    AssertAudibleStereoProgram(pcmSamples, "PCM output provider");
}

static void LiveOutputProviderFollowsMixerMuteAndSolo()
{
    using var service = CreateLiveOutputService(out var outputProvider);
    var mic1Settings = CreateTransparentVoiceSettings();
    var mic2Settings = CreateTransparentVoiceSettings();

    ConfigureTwoMicOutputService(service, mic1Settings, mic2Settings);
    FeedAlternatingStereoBlock(service);
    var mixedSamples = ReadBufferedFloatSamples(outputProvider);
    AssertAudibleStereoProgram(mixedSamples, "unmuted service output");

    ConfigureTwoMicOutputService(service, mic1Settings, mic2Settings, mic2Muted: true);
    FeedAlternatingStereoBlock(service);
    var mutedSamples = ReadBufferedFloatSamples(outputProvider);
    AssertStereoPeaks(
        mutedSamples,
        "muted mic should disappear from the right side of the service output",
        leftMinimum: 0.20f,
        rightMaximum: 0.03f);

    ConfigureTwoMicOutputService(service, mic1Settings, mic2Settings, mic2Soloed: true);
    FeedAlternatingStereoBlock(service);
    var soloedSamples = ReadBufferedFloatSamples(outputProvider);
    AssertStereoPeaks(
        soloedSamples,
        "soloed mic should be the only service output",
        leftMaximum: 0.03f,
        rightMinimum: 0.08f);
}

static void LiveOutputProviderFollowsMixerGainPanPolarityAndDelay()
{
    using var service = CreateLiveOutputService(out var outputProvider);
    var mic1Settings = CreateTransparentVoiceSettings();
    var mic2Settings = CreateTransparentVoiceSettings();

    ConfigureTwoMicOutputService(service, mic1Settings, mic2Settings, mic2Muted: true);
    FeedAlternatingStereoBlock(service);
    var baselineSamples = ReadBufferedFloatSamples(outputProvider);
    var (baselineLeftPeak, _) = MeasureStereoPeaks(baselineSamples);
    Assert(baselineLeftPeak > 0.20f, "baseline left mic should reach the service output before control changes");

    ConfigureTwoMicOutputService(service, mic1Settings, mic2Settings, mic1Pan: 100d, mic2Muted: true);
    FeedAlternatingStereoBlock(service);
    var pannedSamples = ReadBufferedFloatSamples(outputProvider);
    AssertStereoPeaks(
        pannedSamples,
        "pan control should move mic 1 to the right side of the service output",
        leftMaximum: 0.03f,
        rightMinimum: 0.20f);

    using var gainOnlyService = CreateLiveOutputService(out var gainOnlyOutputProvider);
    ConfigureTwoMicOutputService(
        gainOnlyService,
        CreateTransparentVoiceSettings(),
        CreateTransparentVoiceSettings(),
        mic1InputGainDb: -12d,
        mic1Pan: -100d,
        mic2Muted: true);
    FeedAlternatingStereoBlock(gainOnlyService);
    var gainOnlySamples = ReadBufferedFloatSamples(gainOnlyOutputProvider);
    var (gainOnlyLeftPeak, gainOnlyRightPeak) = MeasureStereoPeaks(gainOnlySamples);
    Assert(gainOnlyRightPeak < 0.03f, "input gain test should still be isolated to the left mic");
    Assert(gainOnlyLeftPeak > 0.08f, "input gain should leave an audible reduced signal");
    Assert(gainOnlyLeftPeak < baselineLeftPeak * 0.72f, "input gain should reduce the live service output level");

    using var invertedService = CreateLiveOutputService(out var invertedOutputProvider);
    ConfigureTwoMicOutputService(
        invertedService,
        CreateTransparentVoiceSettings(),
        CreateTransparentVoiceSettings(),
        mic1InputGainDb: -12d,
        mic1Pan: -100d,
        mic1PolarityInverted: true,
        mic2Muted: true);
    FeedAlternatingStereoBlock(invertedService);
    var invertedSamples = ReadBufferedFloatSamples(invertedOutputProvider);
    var (invertedLeftPeak, invertedRightPeak) = MeasureStereoPeaks(invertedSamples);
    var polarityDotProduct = MeasureLeftDotProduct(gainOnlySamples, invertedSamples, startFrame: 0, frameCount: 220);
    Assert(invertedRightPeak < 0.03f, "input gain and polarity test should still be isolated to the left mic");
    Assert(Math.Abs(invertedLeftPeak - gainOnlyLeftPeak) < 0.05f, "polarity should flip the waveform without changing the live output level much");
    Assert(polarityDotProduct < -0.10f, "polarity inversion should produce an opposite live output waveform");

    using var delayedService = CreateLiveOutputService(out var delayedOutputProvider);
    ConfigureTwoMicOutputService(
        delayedService,
        CreateTransparentVoiceSettings(),
        CreateTransparentVoiceSettings(),
        mic1DelayMilliseconds: 1d,
        mic2Muted: true);
    FeedAlternatingStereoBlock(delayedService);
    var delayedSamples = ReadBufferedFloatSamples(delayedOutputProvider);
    var earlyLeftPeak = MeasureLeftPeak(delayedSamples, startFrame: 0, frameCount: 40);
    var lateLeftPeak = MeasureLeftPeak(delayedSamples, startFrame: 64, frameCount: 120);
    Assert(earlyLeftPeak < 0.03f, "per-mic delay should hold back the start of the live output");
    Assert(lateLeftPeak > 0.20f, "per-mic delay should release the delayed live output after the requested time");
}

static void LiveServiceBusMixesAuxiliaryCaptureDevice()
{
    using var service = CreateLiveOutputServiceWithoutPrimaryCapture(out var outputProvider);
    service.ConfigureLiveMix(
        [
            new MicrophoneLiveChannelSettings(
                3,
                7,
                InputChannelMode.MonoSum,
                CreateTransparentVoiceSettings(),
                100d,
                0d,
                100d,
                false,
                false,
                0d,
                true,
                false)
        ],
        new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));

    var auxiliaryCapture = new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1));
    var auxiliaryRuntime = CreateAdditionalCaptureRuntime(service, deviceNumber: 7, auxiliaryCapture);
    var auxiliarySamples = new float[2_400];
    for (var i = 0; i < auxiliarySamples.Length; i++)
    {
        auxiliarySamples[i] = (i / 4) % 2 == 0 ? 0.35f : -0.35f;
    }

    InvokeAdditionalCapture(service, auxiliaryRuntime, auxiliarySamples);

    var primaryCapture = new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
    SetPrivateField<IWaveIn?>(service, "_capture", primaryCapture);
    InvokePrimaryCapture(service, new float[384 * 2]);

    var outputSamples = ReadBufferedFloatSamples(outputProvider);
    AssertStereoPeaks(
        outputSamples,
        "auxiliary capture device should feed the service output bus",
        leftMaximum: 0.03f,
        rightMinimum: 0.12f);
}

static void LiveServiceBusPublishesAuxiliarySyncTelemetry()
{
    using var service = CreateLiveOutputServiceWithoutPrimaryCapture(out _);
    service.ConfigureLiveMix(
        [
            new MicrophoneLiveChannelSettings(
                3,
                7,
                InputChannelMode.MonoSum,
                CreateTransparentVoiceSettings(),
                100d,
                0d,
                0d,
                false,
                false,
                0d,
                true,
                false)
        ],
        new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));

    var auxiliaryCapture = new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1));
    var auxiliaryRuntime = CreateAdditionalCaptureRuntime(service, deviceNumber: 7, auxiliaryCapture);
    SetPrivateField<IWaveIn?>(service, "_capture", new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)));

    SpectrumFrame? latestFrame = null;
    var frameLock = new object();
    service.SpectrumAvailable += (_, frame) =>
    {
        lock (frameLock)
        {
            latestFrame = frame;
        }
    };

    InvokePrimaryCapture(service, new float[384 * 2]);
    Assert(
        WaitForMicLine(line => line.ChannelNumber == 3 && line.SyncUnderflowCount >= 1),
        "auxiliary mic line should publish underflow telemetry when the sync buffer is empty");

    SetPrivateField(service, "_nextSpectrumAnalysisTimestamp", 0L);
    lock (frameLock)
    {
        latestFrame = null;
    }

    var auxiliarySamples = new float[2_400];
    for (var i = 0; i < auxiliarySamples.Length; i++)
    {
        auxiliarySamples[i] = (i / 4) % 2 == 0 ? 0.35f : -0.35f;
    }

    InvokeAdditionalCapture(service, auxiliaryRuntime, auxiliarySamples);
    InvokePrimaryCapture(service, new float[384 * 2]);

    Assert(
        WaitForMicLine(line =>
            line.ChannelNumber == 3
            && line.SyncTargetLatencyMilliseconds >= 34d
            && line.SyncBufferedMilliseconds >= 30d
            && line.SyncUnderflowCount >= 1),
        "auxiliary mic line should publish target latency, buffered latency, and accumulated sync events");

    bool WaitForMicLine(Func<MicrophoneSpectrumLine, bool> predicate)
    {
        return System.Threading.SpinWait.SpinUntil(
            () =>
            {
                lock (frameLock)
                {
                    return latestFrame?.MicrophoneLines.Any(predicate) == true;
                }
            },
            2_000);
    }
}

static void LiveServiceBusRecordsAuxiliaryMicInProgramMix()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        using var service = CreateLiveOutputServiceWithoutPrimaryCapture(out _);
        service.ConfigureLiveMix(
            [
                new MicrophoneLiveChannelSettings(
                    3,
                    7,
                    InputChannelMode.MonoSum,
                    CreateTransparentVoiceSettings(),
                    100d,
                    0d,
                    100d,
                    false,
                    false,
                    0d,
                    true,
                    false)
            ],
            new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));
        service.ConfigureProcessedRecordingSource(ProcessedRecordingSource.ProgramMix, 3);

        var auxiliaryCapture = new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1));
        var auxiliaryRuntime = CreateAdditionalCaptureRuntime(service, deviceNumber: 7, auxiliaryCapture);
        var auxiliarySamples = new float[2_400];
        for (var i = 0; i < auxiliarySamples.Length; i++)
        {
            auxiliarySamples[i] = (i / 4) % 2 == 0 ? 0.35f : -0.35f;
        }

        InvokeAdditionalCapture(service, auxiliaryRuntime, auxiliarySamples);
        SetPrivateField<IWaveIn?>(service, "_capture", new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)));

        var recordingPath = Path.Combine(folder, "aux_program_mix.wav");
        service.StartProcessedAudioRecording(recordingPath);
        InvokePrimaryCapture(service, new float[384 * 2]);
        service.StopProcessedAudioRecording();

        using var reader = new WaveFileReader(recordingPath);
        var recorded = ReadWaveFloatSamples(reader);
        Assert(reader.WaveFormat.Channels == 2, "auxiliary program mix recording should remain stereo");
        Assert(recorded.Length >= 768, "auxiliary program mix recording should write the synced mic block");
        Assert(MeasureLeftPeak(recorded, startFrame: 150, frameCount: int.MaxValue) < 0.03f, "auxiliary hard-right mic should stay out of the left program recording channel");
        Assert(MeasureRightPeak(recorded, startFrame: 150, frameCount: int.MaxValue) > 0.12f, "auxiliary hard-right mic should reach the right program recording channel");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static void LiveServiceBusRecordsSelectedAuxiliaryMicSources()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var rawPath = Path.Combine(folder, "aux_selected_raw.wav");
        var rawRecorded = RecordSelectedAuxiliaryMicSource(rawPath, ProcessedRecordingSource.SelectedMicRawBackup);
        Assert(rawRecorded.Length >= 384, "selected auxiliary raw backup should write the synced mic block");
        Assert(rawRecorded[0] > 0.20f, "selected auxiliary raw backup should preserve the headset mic sample polarity");
        Assert(rawRecorded[1] < -0.20f, "selected auxiliary raw backup should preserve following raw samples");

        var processedPath = Path.Combine(folder, "aux_selected_processed.wav");
        var processedRecorded = RecordSelectedAuxiliaryMicSource(processedPath, ProcessedRecordingSource.SelectedMicProcessed);
        Assert(processedRecorded.Length >= 384, "selected auxiliary processed mic should write the synced mic block");
        Assert(processedRecorded[0] < -0.12f, "selected auxiliary processed mic should include polarity inversion");
        Assert(processedRecorded[1] > 0.12f, "selected auxiliary processed mic should preserve inverted following samples");
        Assert(MeasureMonoDotProduct(rawRecorded, processedRecorded, 220) < -2f, "raw and processed auxiliary recordings should be opposite-polarity paths");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static float[] RecordSelectedAuxiliaryMicSource(string recordingPath, ProcessedRecordingSource source)
{
    using var service = CreateLiveOutputServiceWithoutPrimaryCapture(out _);
    service.ConfigureLiveMix(
        [
            new MicrophoneLiveChannelSettings(
                3,
                7,
                InputChannelMode.MonoSum,
                CreateTransparentVoiceSettings(),
                100d,
                0d,
                0d,
                true,
                false,
                0d,
                true,
                false)
        ],
        new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));
    service.ConfigureProcessedRecordingSource(source, 3);

    var auxiliaryCapture = new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1));
    var auxiliaryRuntime = CreateAdditionalCaptureRuntime(service, deviceNumber: 7, auxiliaryCapture);
    var auxiliarySamples = new float[2_400];
    for (var i = 0; i < auxiliarySamples.Length; i++)
    {
        auxiliarySamples[i] = i % 2 == 0 ? 0.35f : -0.35f;
    }

    InvokeAdditionalCapture(service, auxiliaryRuntime, auxiliarySamples);
    SetPrivateField<IWaveIn?>(service, "_capture", new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)));

    service.StartProcessedAudioRecording(recordingPath);
    InvokePrimaryCapture(service, new float[384 * 2]);
    service.StopProcessedAudioRecording();

    using var reader = new WaveFileReader(recordingPath);
    Assert(reader.WaveFormat.Channels == 1, "selected auxiliary mic recordings should be mono");
    return ReadWaveFloatSamples(reader);
}

static void FeedLiveOutputProvider(BufferedWaveProvider outputProvider)
{
    using var service = CreateLiveOutputServiceForProvider(outputProvider);
    ConfigureTwoMicOutputService(service, CreateTransparentVoiceSettings(), CreateTransparentVoiceSettings());
    FeedAlternatingStereoBlock(service);
}

static MicrophoneSpectrumService CreateLiveOutputService(out BufferedWaveProvider outputProvider)
{
    var service = CreateLiveOutputServiceWithoutPrimaryCapture(out outputProvider);
    SetPrivateField<IWaveIn?>(service, "_capture", new FakeWaveIn(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)));
    return service;
}

static MicrophoneSpectrumService CreateLiveOutputServiceForProvider(BufferedWaveProvider outputProvider)
{
    var service = new MicrophoneSpectrumService();
    var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    var capture = new FakeWaveIn(waveFormat);
    SetPrivateField<IWaveIn?>(service, "_capture", capture);
    SetPrivateField(service, "_currentDeviceNumber", 0);
    SetPrivateField(service, "_activeSampleRate", 48_000);
    SetPrivateField(service, "_inputChannelMode", InputChannelMode.MonoSum);
    SetPrivateField(service, "_processedOutputEnabled", 1);
    SetPrivateField(service, "_processedOutputProvider", outputProvider);
    return service;
}

static MicrophoneSpectrumService CreateLiveOutputServiceWithoutPrimaryCapture(out BufferedWaveProvider outputProvider)
{
    outputProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2))
    {
        ReadFully = false
    };
    var service = new MicrophoneSpectrumService();
    SetPrivateField(service, "_currentDeviceNumber", 0);
    SetPrivateField(service, "_activeSampleRate", 48_000);
    SetPrivateField(service, "_inputChannelMode", InputChannelMode.MonoSum);
    SetPrivateField(service, "_processedOutputEnabled", 1);
    SetPrivateField(service, "_processedOutputProvider", outputProvider);
    return service;
}

static void ConfigureTwoMicOutputService(
    MicrophoneSpectrumService service,
    VoiceProcessorSettings mic1Settings,
    VoiceProcessorSettings mic2Settings,
    double mic1InputGainDb = 0d,
    double mic1Pan = -100d,
    bool mic1PolarityInverted = false,
    double mic1DelayMilliseconds = 0d,
    bool mic2Muted = false,
    bool mic2Soloed = false,
    double mic2InputGainDb = 0d,
    double mic2Pan = 100d,
    bool mic2PolarityInverted = false,
    double mic2DelayMilliseconds = 0d)
{
    service.ConfigureLiveMix(
        [
            new MicrophoneLiveChannelSettings(
                1,
                0,
                InputChannelMode.Input1Left,
                mic1Settings,
                100d,
                mic1InputGainDb,
                mic1Pan,
                mic1PolarityInverted,
                false,
                mic1DelayMilliseconds,
                true,
                false),
            new MicrophoneLiveChannelSettings(
                2,
                0,
                InputChannelMode.Input2Right,
                mic2Settings,
                100d,
                mic2InputGainDb,
                mic2Pan,
                mic2PolarityInverted,
                mic2Soloed,
                mic2DelayMilliseconds,
                true,
                mic2Muted)
        ],
        new MixBusSettings(100d, false, false, -1d, MixBusOutputMode.Stereo));
}

static void FeedAlternatingStereoBlock(MicrophoneSpectrumService service)
{
    const int frameCount = 384;
    var stereoSamples = new float[frameCount * 2];
    for (var frame = 0; frame < frameCount; frame++)
    {
        var sign = frame % 2 == 0 ? 1f : -1f;
        stereoSamples[frame * 2] = 0.50f * sign;
        stereoSamples[frame * 2 + 1] = 0.25f * sign;
    }

    var buffer = MemoryMarshal.AsBytes(stereoSamples.AsSpan()).ToArray();
    InvokePrimaryCaptureBytes(service, buffer);
}

static void InvokePrimaryCapture(MicrophoneSpectrumService service, float[] interleavedSamples)
{
    InvokePrimaryCaptureBytes(service, MemoryMarshal.AsBytes(interleavedSamples.AsSpan()).ToArray());
}

static void InvokePrimaryCaptureBytes(MicrophoneSpectrumService service, byte[] buffer)
{
    var method = typeof(MicrophoneSpectrumService).GetMethod(
        "CaptureDataAvailable",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method is not null, "capture callback should be available for output provider service integration test");
    method!.Invoke(service, [null, new WaveInEventArgs(buffer, buffer.Length)]);
}

static object CreateAdditionalCaptureRuntime(MicrophoneSpectrumService service, int deviceNumber, IWaveIn capture)
{
    var runtimeType = typeof(MicrophoneSpectrumService).GetNestedType(
        "AdditionalCaptureRuntime",
        BindingFlags.NonPublic);
    Assert(runtimeType is not null, "additional capture runtime should be available for service integration test");
    var runtime = Activator.CreateInstance(
        runtimeType!,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [service, deviceNumber],
        culture: null);
    Assert(runtime is not null, "additional capture runtime should be constructable for service integration test");
    var captureProperty = runtimeType!.GetProperty(
        "Capture",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(captureProperty is not null, "additional capture runtime should expose capture property");
    captureProperty!.SetValue(runtime, capture);
    return runtime!;
}

static void InvokeAdditionalCapture(MicrophoneSpectrumService service, object runtime, float[] monoSamples)
{
    var buffer = MemoryMarshal.AsBytes(monoSamples.AsSpan()).ToArray();
    var method = typeof(MicrophoneSpectrumService).GetMethod(
        "AdditionalCaptureDataAvailable",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method is not null, "additional capture callback should be available for service integration test");
    method!.Invoke(service, [runtime, new WaveInEventArgs(buffer, buffer.Length)]);
}

static float[] ReadBufferedFloatSamples(BufferedWaveProvider provider)
{
    var bytes = new byte[provider.BufferedBytes];
    var read = provider.Read(bytes, 0, bytes.Length);
    return MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, read)).ToArray();
}

static float[] ReadBufferedPcm16Samples(BufferedWaveProvider provider)
{
    var bytes = new byte[provider.BufferedBytes];
    var read = provider.Read(bytes, 0, bytes.Length);
    var pcmSamples = MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, read));
    var samples = new float[pcmSamples.Length];
    for (var i = 0; i < samples.Length; i++)
    {
        samples[i] = pcmSamples[i] / 32768f;
    }

    return samples;
}

static void AssertAudibleStereoProgram(IReadOnlyList<float> samples, string routeName)
{
    Assert(samples.Count >= 512, $"{routeName} should receive the live output block");

    var (leftPeak, rightPeak) = MeasureStereoPeaks(samples);

    Assert(leftPeak > 0.20f, $"{routeName} should contain audible left program mix samples");
    Assert(rightPeak > 0.08f, $"{routeName} should contain audible right program mix samples");
    Assert(leftPeak > rightPeak * 1.35f, $"{routeName} should preserve the left/right program balance");
}

static void AssertStereoPeaks(
    IReadOnlyList<float> samples,
    string message,
    float? leftMinimum = null,
    float? leftMaximum = null,
    float? rightMinimum = null,
    float? rightMaximum = null)
{
    Assert(samples.Count >= 512, $"{message}: output block should be present");
    var (leftPeak, rightPeak) = MeasureStereoPeaks(samples);
    if (leftMinimum is not null)
    {
        Assert(leftPeak >= leftMinimum.Value, $"{message}: left peak {leftPeak:0.000} should be >= {leftMinimum:0.000}");
    }

    if (leftMaximum is not null)
    {
        Assert(leftPeak <= leftMaximum.Value, $"{message}: left peak {leftPeak:0.000} should be <= {leftMaximum:0.000}");
    }

    if (rightMinimum is not null)
    {
        Assert(rightPeak >= rightMinimum.Value, $"{message}: right peak {rightPeak:0.000} should be >= {rightMinimum:0.000}");
    }

    if (rightMaximum is not null)
    {
        Assert(rightPeak <= rightMaximum.Value, $"{message}: right peak {rightPeak:0.000} should be <= {rightMaximum:0.000}");
    }
}

static (float LeftPeak, float RightPeak) MeasureStereoPeaks(IReadOnlyList<float> samples)
{
    return (
        MeasureLeftPeak(samples, startFrame: 150, frameCount: int.MaxValue),
        MeasureRightPeak(samples, startFrame: 150, frameCount: int.MaxValue));
}

static float MeasureLeftPeak(IReadOnlyList<float> samples, int startFrame, int frameCount)
{
    return MeasureChannelPeak(samples, channel: 0, startFrame, frameCount);
}

static float MeasureRightPeak(IReadOnlyList<float> samples, int startFrame, int frameCount)
{
    return MeasureChannelPeak(samples, channel: 1, startFrame, frameCount);
}

static float MeasureChannelPeak(IReadOnlyList<float> samples, int channel, int startFrame, int frameCount)
{
    var start = Math.Clamp(startFrame, 0, Math.Max(0, samples.Count / 2)) * 2 + Math.Clamp(channel, 0, 1);
    var maxExclusive = frameCount == int.MaxValue
        ? samples.Count
        : Math.Min(samples.Count, start + Math.Max(0, frameCount) * 2);
    var peak = 0f;
    for (var i = start; i < maxExclusive; i += 2)
    {
        peak = Math.Max(peak, Math.Abs(samples[i]));
    }

    return peak;
}

static float MeasureLeftDotProduct(IReadOnlyList<float> first, IReadOnlyList<float> second, int startFrame, int frameCount)
{
    var start = Math.Clamp(startFrame, 0, Math.Min(first.Count, second.Count) / 2) * 2;
    var maxExclusive = Math.Min(Math.Min(first.Count, second.Count), start + Math.Max(0, frameCount) * 2);
    var dotProduct = 0f;
    for (var i = start; i < maxExclusive; i += 2)
    {
        dotProduct += first[i] * second[i];
    }

    return dotProduct;
}

static float MeasureMonoDotProduct(IReadOnlyList<float> first, IReadOnlyList<float> second, int sampleCount)
{
    var count = Math.Min(Math.Min(first.Count, second.Count), Math.Max(0, sampleCount));
    var dotProduct = 0f;
    for (var i = 0; i < count; i++)
    {
        dotProduct += first[i] * second[i];
    }

    return dotProduct;
}

static void ProcessedAudioConverterWritesOutputFormats()
{
    var monoSamples = new[] { 0.25f, -0.5f, float.NaN, 2f };
    var floatBytes = new byte[ProcessedAudioSampleConverter.GetStereoFloat32ByteCount(monoSamples.Length, 1)];
    var floatByteCount = ProcessedAudioSampleConverter.WriteStereoFloat32(
        monoSamples,
        sourceChannelCount: 1,
        floatBytes,
        sample => sample * 0.5f);
    var floatOutput = MemoryMarshal.Cast<byte, float>(floatBytes.AsSpan(0, floatByteCount)).ToArray();

    AssertSequenceEqual(
        new[] { 0.125f, 0.125f, -0.25f, -0.25f, 0f, 0f, 0.5f, 0.5f },
        floatOutput,
        "mono program output should duplicate sanitized samples to stereo float");

    var stereoSamples = new[] { 0.2f, 0.4f, -2f, float.NaN, 0.5f, -0.25f };
    floatBytes = new byte[ProcessedAudioSampleConverter.GetStereoFloat32ByteCount(stereoSamples.Length, 2)];
    floatByteCount = ProcessedAudioSampleConverter.WriteStereoFloat32(stereoSamples, 2, floatBytes);
    floatOutput = MemoryMarshal.Cast<byte, float>(floatBytes.AsSpan(0, floatByteCount)).ToArray();

    AssertSequenceEqual(
        new[] { 0.2f, 0.4f, -1f, 0f, 0.5f, -0.25f },
        floatOutput,
        "stereo program output should keep left/right samples and sanitize bad values");

    var pcmBytes = new byte[ProcessedAudioSampleConverter.GetStereoPcm16ByteCount(2, 1)];
    uint ditherState = 0;
    var pcmByteCount = ProcessedAudioSampleConverter.WriteStereoPcm16([1f, -1f], 1, pcmBytes, ref ditherState);
    var pcmOutput = MemoryMarshal.Cast<byte, short>(pcmBytes.AsSpan(0, pcmByteCount)).ToArray();

    AssertSequenceEqual(
        new[] { short.MaxValue, short.MaxValue, short.MinValue, short.MinValue },
        pcmOutput,
        "PCM fallback output should duplicate mono samples and quantize full scale safely");
}

static void ProcessedAudioConverterRechannelsRecordingSources()
{
    var stereoProgram = new[] { 0.8f, 0.2f, -0.5f, -1.5f, float.NaN, 0.6f };
    var monoRecording = new float[ProcessedAudioSampleConverter.GetConvertedSampleCount(stereoProgram.Length, 2, 1)];
    var written = ProcessedAudioSampleConverter.WriteChannelCount(stereoProgram, 2, 1, monoRecording);

    Assert(written == monoRecording.Length, "recording converter should report the mono recording sample count");
    AssertSequenceEqual(
        new[] { 0.5f, -0.75f, 0.3f },
        monoRecording,
        "selected mono recording should average sanitized stereo program frames");

    var selectedMic = new[] { 0.25f, -0.25f };
    var stereoRecording = new float[ProcessedAudioSampleConverter.GetConvertedSampleCount(selectedMic.Length, 1, 2)];
    written = ProcessedAudioSampleConverter.WriteChannelCount(selectedMic, 1, 2, stereoRecording);

    Assert(written == stereoRecording.Length, "recording converter should report the stereo recording sample count");
    AssertSequenceEqual(
        new[] { 0.25f, 0.25f, -0.25f, -0.25f },
        stereoRecording,
        "mono selected mic recordings should expand cleanly when written to a stereo target");
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

    var recordingOutput = SpectrumFrameRouter.CreateProgramOutputFrame(frame, lines[2].Magnitudes);
    AssertSequenceEqual(frame.Magnitudes, recordingOutput.Magnitudes, "program line should stay the final output when a recording reference is present");
    AssertSequenceEqual(lines[2].Magnitudes, recordingOutput.RawMagnitudes, "recording reference line should carry the selected recording source");
    Assert(recordingOutput.MicrophoneLines.Count == 0, "recording reference should not bring the ten individual mic lines back");
}

static void Dx12GraphModesMatchSelectedMicAndMixerRoles()
{
    var selectedMicModeMethod = typeof(EqualizerWindow).GetMethod(
        "ResolveSelectedMicSpectrumGraphMode",
        BindingFlags.NonPublic | BindingFlags.Static);
    var mixingModeMethod = typeof(EqualizerWindow).GetMethod(
        "ResolveMixingSpectrumGraphMode",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(selectedMicModeMethod is not null, "selected mic DX12 graph mode resolver should be available");
    Assert(mixingModeMethod is not null, "mixing DX12 graph mode resolver should be available");

    var selectedMicMode = (Direct3D12AudioGraphMode)selectedMicModeMethod!.Invoke(null, [])!;
    var mixingMode = (Direct3D12AudioGraphMode)mixingModeMethod!.Invoke(null, [])!;

    Assert(
        selectedMicMode == Direct3D12AudioGraphMode.SelectedMicSpectrum,
        "Mic/DSP spectrum should use the selected-mic DX12 mode so hover bands and the selected mic frame line up");
    Assert(
        mixingMode == Direct3D12AudioGraphMode.ProgramOutputSpectrum,
        "Mixing spectrum should use the program-output DX12 mode so it can draw program and recording reference lines");
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

static void NAudioPeakMeterReportsProviderPeaks()
{
    var source = new ArraySampleProvider(
        [-0.25f, 0.75f, 0.10f, -0.50f],
        WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1));
    var meter = new NaudioPeakMeterSampleProvider(source, samplesPerNotification: 2);
    var output = new float[2];

    var read = meter.Read(output, 0, output.Length);

    Assert(read == output.Length, "metered provider should read through the source provider");
    Assert(Math.Abs(meter.PeakLevel - 0.75d) < 0.0001d, "NAudio meter should report the strongest absolute sample in the notification");
}

static void SpectrumLinesCarryNaudioMeteredPeaks()
{
    var line = new MicrophoneSpectrumLine(
        1,
        [0.2d],
        0.4d,
        rawPeakLevel: 0.9d,
        meteredPeakLevel: 0.35d);

    Assert(Math.Abs(line.MeteredPeakLevel - 0.35d) < 0.0001d, "spectrum line should carry NAudio metered channel peak separately from raw input peak");
    Assert(Math.Abs(line.RawPeakLevel - 0.9d) < 0.0001d, "raw input peak should remain available for input coaching and fallback metering");
}

static void MixerStripMetersIgnoreMutedRawInput()
{
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var meterMethod = ExtractSourceBetween(
        windowCode,
        "    private void UpdateMixerChannelMeters(SpectrumFrame frame)",
        "    private static bool IsMixerChannelAudibleInProgram(MicChannelStrip channel, bool hasSolo)");
    var audibilityHelper = ExtractSourceBetween(
        windowCode,
        "    private static bool IsMixerChannelAudibleInProgram(MicChannelStrip channel, bool hasSolo)",
        "    private void UpdateMixBusStatus(SpectrumFrame frame)");

    Assert(meterMethod.Contains("channel.ClearLevelMeter();", StringComparison.Ordinal), "muted or solo-gated mixer strips should clear their visible meters");
    Assert(!meterMethod.Contains("line.RawPeakLevel", StringComparison.Ordinal), "mixer strips should not fall back to raw capture peaks when program metering is silent");
    Assert(!meterMethod.Contains("line.RawRmsLevel", StringComparison.Ordinal), "mixer strips should not show raw capture RMS as program activity");
    Assert(meterMethod.Contains("line.MeteredPeakLevel > 0d", StringComparison.Ordinal), "mixer strips should prefer the post-fader NAudio meter");
    Assert(meterMethod.Contains("line.PeakLevel", StringComparison.Ordinal), "audible mixer strips can fall back to processed channel signal");
    Assert(audibilityHelper.Contains("LiveMixAudibility.IsAudible", StringComparison.Ordinal), "mixer strip metering should follow the same mute and solo rules as the program bus");
    Assert(audibilityHelper.Contains("channel.VolumePercent > 0.1d", StringComparison.Ordinal), "zero-volume mixer strips should not show as program-audible");
}

static void LiveProgramMixBusCombinesTenMicFeeds()
{
    var bus = new LiveProgramMixBus(48_000);
    var mics = new LiveMicBlockSampleProvider[10];

    for (var i = 0; i < mics.Length; i++)
    {
        var mic = new LiveMicBlockSampleProvider(48_000);
        var fader = new VolumeSampleProvider(mic) { Volume = 1f };
        var pan = new StereoPanSampleProvider(fader)
        {
            Pan = i % 2 == 0 ? -1d : 1d
        };

        mics[i] = mic;
        bus.AddMicInput(pan);
        mic.SetBlock([(i + 1) * 0.01f, -(i + 1) * 0.01f]);
    }

    var output = new float[6];
    var read = bus.Read(output, 0, output.Length);

    Assert(read == output.Length, "live program bus should stay full for real-time playback");
    Assert(bus.InputCount == 10, "live program bus should accept all ten mic feeds");
    Assert(Math.Abs(output[0] - 0.25f) < 0.0001f, "odd-numbered hard-left mics should sum into the left program channel");
    Assert(Math.Abs(output[1] - 0.30f) < 0.0001f, "even-numbered hard-right mics should sum into the right program channel");
    Assert(Math.Abs(output[2] + 0.25f) < 0.0001f, "left channel should preserve following mic samples");
    Assert(Math.Abs(output[3] + 0.30f) < 0.0001f, "right channel should preserve following mic samples");
    Assert(Math.Abs(output[4]) < 0.0001f && Math.Abs(output[5]) < 0.0001f, "exhausted live mic blocks should pad with silence");
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

static void MixerStripClicksSelectChannelsCheaply()
{
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var windowXaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var handler = ExtractSourceBetween(
        windowCode,
        "    private async void MixerChannelStripPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)",
        "    private async Task SetActiveMicChannelAsync(MicChannelStrip channel, bool restartAudio, bool refreshEditor = true)");

    Assert(windowXaml.Contains("PreviewMouseLeftButtonDown=\"MixerChannelStripPreviewMouseLeftButtonDown\"", StringComparison.Ordinal), "mixer strip should select channels from any click inside the strip");
    Assert(!handler.Contains("IsMixerControlInteraction", StringComparison.Ordinal), "mixer strip controls should still select their channel instead of leaving the active mic stuck");
    Assert(handler.Contains("SetActiveMicChannelAsync(channel, restartAudio: false, refreshEditor: false)", StringComparison.Ordinal), "mixer strip selection should avoid rebinding the full mic editor");
    Assert(windowXaml.Contains("x:Name=\"SelectedMixerInputPanel\"", StringComparison.Ordinal), "mixer selected input panel should have its own active-channel data context");
    Assert(!windowXaml.Contains("DataContext=\"{Binding ElementName=MicChannelComboBox, Path=SelectedItem}\"", StringComparison.Ordinal), "mixer selected input panel should not depend on the Mic/DSP tab combo box selection");
}

static void ActiveMicSelectionAvoidsSynchronousFormatProbe()
{
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var method = ExtractSourceBetween(
        windowCode,
        "    private void ApplyActiveMicChannelToUi()",
        "    private void RefreshInputChannelOptionsForActiveDevice(AudioDeviceFormat? selectedDeviceFormat)");

    Assert(method.Contains("QueueSelectedDeviceFormatRefresh(_selectedDevice);", StringComparison.Ordinal), "active mic selection should refresh device format asynchronously");
    Assert(!method.Contains("GetSelectedDeviceFormat();", StringComparison.Ordinal), "active mic selection should not synchronously probe the selected device format on the UI thread");

    var selectionMethod = ExtractSourceBetween(
        windowCode,
        "    private async Task SetActiveMicChannelAsync(MicChannelStrip channel, bool restartAudio, bool refreshEditor = true)",
        "    private void ApplyActiveMicChannelToUi()");
    Assert(selectionMethod.Contains("ApplyActiveMicChannelSelectionToMixerUi();", StringComparison.Ordinal), "mixer-side mic selection should update only selection state");
    Assert(selectionMethod.Contains("UpdateLiveMixControlsFromChannels();", StringComparison.Ordinal), "mixer-side mic selection should use controls-only live mix updates");

    var mixerSelectionHelper = ExtractSourceBetween(
        windowCode,
        "    private void ApplyActiveMicChannelSelectionToMixerUi()",
        "    private void ApplySelectedMixerInputPanelToUi()");
    Assert(mixerSelectionHelper.Contains("ApplySelectedMixerInputPanelToUi();", StringComparison.Ordinal), "mixer-side mic selection should refresh the mixer selected-input panel");

    var panelHelper = ExtractSourceBetween(
        windowCode,
        "    private void ApplySelectedMixerInputPanelToUi()",
        "    private void QueueSelectedDeviceFormatRefresh(AudioInputDevice? selectedDevice)");
    Assert(panelHelper.Contains("SelectedMixerInputPanel.DataContext = _activeMicChannel;", StringComparison.Ordinal), "mixer selected-input panel should bind directly to the active channel");
}

static void MixerChannelControlsDebounceStatePersistence()
{
    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowCode.Contains("private readonly DispatcherTimer _appStatePersistTimer", StringComparison.Ordinal), "window should have a debounced app-state persistence timer");
    Assert(windowCode.Contains("private void ScheduleAppStatePersist()", StringComparison.Ordinal), "window should expose a debounced app-state persistence helper");

    var checkBoxHandler = ExtractSourceBetween(
        windowCode,
        "    private void MixerChannelControlChanged(object sender, RoutedEventArgs e)",
        "    private void MixerChannelControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)");
    var sliderHandler = ExtractSourceBetween(
        windowCode,
        "    private void MixerChannelControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)",
        "    private void ResetSelectedMixerChannelClicked(object sender, RoutedEventArgs e)");

    Assert(checkBoxHandler.Contains("ScheduleAppStatePersist();", StringComparison.Ordinal), "mixer checkbox changes should debounce app-state saves");
    Assert(sliderHandler.Contains("ScheduleAppStatePersist();", StringComparison.Ordinal), "mixer slider changes should debounce app-state saves");
    Assert(checkBoxHandler.Contains("UpdateLiveMixControlsFromChannels();", StringComparison.Ordinal), "mixer checkbox changes should use controls-only live mix updates");
    Assert(sliderHandler.Contains("UpdateLiveMixControlsFromChannels();", StringComparison.Ordinal), "mixer slider changes should use controls-only live mix updates");
    Assert(!checkBoxHandler.Contains("PersistAppState();", StringComparison.Ordinal), "mixer checkbox changes should not synchronously save app state on the UI thread");
    Assert(!sliderHandler.Contains("PersistAppState();", StringComparison.Ordinal), "mixer slider changes should not synchronously save app state on the UI thread");
    Assert(!checkBoxHandler.Contains("ConfigureLiveMixFromChannels();", StringComparison.Ordinal), "mixer checkbox changes should not fully reconfigure the live mix");
    Assert(!sliderHandler.Contains("ConfigureLiveMixFromChannels();", StringComparison.Ordinal), "mixer slider changes should not fully reconfigure the live mix");
}

static void MixerVolumeControlsMarkAndSnapUnity()
{
    var xaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    var mixerFaderStyle = ExtractSourceBetween(
        xaml,
        "    <Style x:Key=\"MixerFaderSlider\"",
        "    <Style x:Key=\"ProcessedOutputToggle\"");
    Assert(mixerFaderStyle.Contains("Ticks=\"{TemplateBinding Ticks}\"", StringComparison.Ordinal), "mixer fader style should render slider-provided tick marks");
    Assert(mixerFaderStyle.Contains("IsDirectionReversed=\"True\"", StringComparison.Ordinal), "mixer fader unity tick marks should follow the fader direction");
    Assert(!mixerFaderStyle.Contains("VerticalAlignment=\"Center\" Margin=\"5,0\"", StringComparison.Ordinal), "mixer faders should not use a fixed center marker for unity");

    var mixingTab = ExtractSourceBetween(
        xaml,
        "      <TabItem x:Name=\"MixingTabItem\" Header=\"Mixing\">",
        "      <TabItem x:Name=\"MidiTabItem\" Header=\"MIDI\" Visibility=\"Collapsed\">");
    Assert(mixingTab.Contains("x:Name=\"MasterVolumeSlider\"", StringComparison.Ordinal), "mixer tab should expose the master volume slider");
    Assert(mixingTab.Contains("x:Name=\"MasterVolumeSlider\" Width=\"128\" Minimum=\"0\" Maximum=\"150\" Value=\"100\" Ticks=\"100\"", StringComparison.Ordinal), "master volume should mark unity at 100 percent");
    Assert(mixingTab.Contains("Style=\"{StaticResource MixerFaderSlider}\" Minimum=\"0\" Maximum=\"150\"", StringComparison.Ordinal)
        && mixingTab.Contains("Value=\"{Binding VolumePercent, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\" Ticks=\"100\"", StringComparison.Ordinal), "channel faders should mark unity at 100 percent");

    var snapMethod = typeof(EqualizerWindow).GetMethod(
        "SnapMixerUnityVolumePercent",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(snapMethod is not null, "mixer unity snap helper should be available");
    double Snap(double value) => (double)snapMethod!.Invoke(null, [value])!;

    Assert(Math.Abs(Snap(98d) - 100d) < 0.0001d, "mixer volume should snap up to unity inside the magnet zone");
    Assert(Math.Abs(Snap(102d) - 100d) < 0.0001d, "mixer volume should snap down to unity inside the magnet zone");
    Assert(Math.Abs(Snap(97.9d) - 97.9d) < 0.0001d, "mixer volume should remain free below the magnet zone");
    Assert(Math.Abs(Snap(102.1d) - 102.1d) < 0.0001d, "mixer volume should remain free above the magnet zone");
    Assert(double.IsNaN(Snap(double.NaN)), "mixer volume snap should leave non-finite values for existing clamping paths");

    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var mixerSliderHandler = ExtractSourceBetween(
        windowCode,
        "    private void MixerChannelControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)",
        "    private void ResetSelectedMixerChannelClicked");
    var masterSliderHandler = ExtractSourceBetween(
        windowCode,
        "    private void MasterMixControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)",
        "    private void MasterOutputModeChanged");
    Assert(mixerSliderHandler.Contains("TrySnapMixerUnitySlider(sender)", StringComparison.Ordinal), "channel faders should apply the unity magnet before updating the live mix");
    Assert(masterSliderHandler.Contains("TrySnapMixerUnitySlider(sender)", StringComparison.Ordinal), "master volume should apply the unity magnet before rebuilding the live mix");
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

static void StereoAudioDelayLinePreservesLeftAndRight()
{
    var delay = new AudioStereoDelayLine(8_000, 10d);
    var samples = new[] { 1f, 10f, 2f, 20f, 3f, 30f };

    delay.Process(samples, 0.25d);

    AssertSequenceEqual([0f, 0f, 0f, 0f, 1f, 10f], samples, "stereo delay should delay by frames without swapping channels");

    delay.Reset();
    samples = [4f, 40f];
    delay.Process(samples, 0d);

    AssertSequenceEqual([4f, 40f], samples, "zero stereo delay after reset should pass both channels through");
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

static void AudioSyncBufferPreservesInterleavedStereo()
{
    var buffer = new AudioSyncBuffer(48_000, TimeSpan.Zero, TimeSpan.FromMilliseconds(40), channelCount: 2);
    var output = new float[8];

    buffer.Write([0.1f, -0.1f, 0.2f, -0.2f, 0.3f, -0.3f, 0.4f, -0.4f], 48_000);
    Assert(buffer.ReadAligned(output), "stereo sync buffer should read when enough stereo frames are available");

    AssertSequenceEqual([0.1f, -0.1f, 0.2f, -0.2f, 0.3f, -0.3f, 0.4f, -0.4f], output, "stereo sync buffer should keep left and right interleaved");
}

static void AudioSyncBufferResamplesAndTrimsDrift()
{
    var resampleBuffer = new AudioSyncBuffer(48_000, TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
    var source = Enumerable.Range(0, 441).Select(index => index / 440f).ToArray();
    var output = new float[480];

    resampleBuffer.Write(source, 44_100);
    Assert(resampleBuffer.ReadAligned(output), "resampled buffer should provide the requested output");
    Assert(Math.Abs(output[0]) < 0.0001f, "resampled output should start at the first source sample");
    Assert(output.Max() > 0.9f, "resampled output should preserve the high end of the source ramp");
    Assert(output.Skip(output.Length / 2).Average() > 0.45f, "resampled output should keep the ramp energy through the later samples");
    Assert(output.All(float.IsFinite), "resampled output should stay finite");

    var driftBuffer = new AudioSyncBuffer(1_000, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
    driftBuffer.Write(Enumerable.Repeat(0.25f, 100).ToArray(), 1_000);
    var beforeTrimCount = driftBuffer.BufferedSamples;
    Assert(driftBuffer.ReadAligned(new float[5]), "drift buffer should still produce output after trimming");
    Assert(driftBuffer.DriftTrimCount > 0, "excess buffered audio should be trimmed as drift");
    Assert(driftBuffer.BufferedSamples < beforeTrimCount, "drift trim should reduce buffered audio");
}

static void AudioSyncBufferUsesNAudioWdlResampler()
{
    const int sourceSampleRate = 44_100;
    const int targetSampleRate = 48_000;
    var source = GenerateSine(sourceSampleRate, 1_000, 0.35, 0.1);
    var outputLength = NAudioSampleRateConverter.EstimateOutputSampleCount(source.Length, sourceSampleRate, targetSampleRate, channelCount: 1);
    var output = new float[outputLength];

    Assert(NAudioSampleRateConverter.TryResampleInterleaved(source, sourceSampleRate, targetSampleRate, 1, output, 0, output.Length, out var written), "NAudio WDL resampler should convert mixed sample-rate input blocks");
    Assert(written > outputLength * 0.9, "NAudio WDL resampler should produce the expected amount of output audio");
    Assert(output.Take(written).All(float.IsFinite), "NAudio WDL resampler output should stay finite");

    var expectedTone = CalculateToneMagnitude(output, targetSampleRate, 1_000, targetSampleRate / 100, Math.Min(2048, written - targetSampleRate / 100));
    var wrongTone = CalculateToneMagnitude(output, targetSampleRate, 1_600, targetSampleRate / 100, Math.Min(2048, written - targetSampleRate / 100));
    Assert(expectedTone > wrongTone * 3d, "resampled output should preserve the source tone at the target sample rate");

    var converterSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "NAudioSampleRateConverter.cs")));
    var syncBufferSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "AudioSyncBuffer.cs")));
    Assert(converterSource.Contains("WdlResamplingSampleProvider", StringComparison.Ordinal), "sample-rate conversion should use NAudio's WDL resampler");
    Assert(syncBufferSource.Contains("NAudioSampleRateConverter.TryResampleInterleaved", StringComparison.Ordinal), "auxiliary sync buffers should use the NAudio resampler before falling back");
}

static void NAudioFileAnalyzerReportsRecordingQualityDetails()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        const int sampleRate = 48_000;
        var path = Path.Combine(folder, "analysis.wav");
        var samples = new float[sampleRate / 10 + sampleRate / 10 + sampleRate / 20];
        var toneStart = sampleRate / 10;
        var toneLength = sampleRate / 10;
        for (var i = 0; i < toneLength; i++)
        {
            samples[toneStart + i] = (float)(Math.Sin(i * Math.PI * 2d * 440d / sampleRate) * 0.2d);
        }

        samples[toneStart + 100] = 1f;
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        Assert(AudioFileAnalyzer.TryAnalyze(path, out var analysis, out var status), status);
        Assert(analysis.SampleRate == sampleRate, "audio analyzer should preserve the file sample rate");
        Assert(analysis.Channels == 1, "audio analyzer should preserve the file channel count");
        Assert(analysis.BitsPerSample == 32, "float WAV analysis should report 32-bit samples");
        Assert(analysis.SampleCount == samples.Length, "audio analyzer should account for every mono sample");
        Assert(!analysis.IsPartial, "uncapped audio analysis should scan the whole file");
        Assert(analysis.AnalyzedDuration >= TimeSpan.FromMilliseconds(240), "uncapped audio analysis should report the scanned duration");
        Assert(analysis.PeakLevel >= 0.999f, "audio analyzer should report clipped peaks");
        Assert(analysis.ClippedSamples == 1, "audio analyzer should count clipped samples");
        Assert(analysis.RmsLevel > 0.05d, "audio analyzer should report a useful RMS level");
        Assert(analysis.LeadingSilence >= TimeSpan.FromMilliseconds(95), "audio analyzer should measure leading silence");
        Assert(analysis.TrailingSilence >= TimeSpan.FromMilliseconds(45), "audio analyzer should measure trailing silence");
        Assert(analysis.WaveformPeaks.Count is > 10 and <= 80, "audio analyzer should expose bounded waveform buckets");
        Assert(analysis.BrowserSummary.Contains("peak", StringComparison.OrdinalIgnoreCase), "recording browser summary should include peak information");
        Assert(analysis.DetailSummary.Contains("silence", StringComparison.OrdinalIgnoreCase), "recording detail summary should include silence information");

        Assert(AudioFileAnalyzer.TryAnalyze(path, out var partialAnalysis, out var partialStatus, TimeSpan.FromMilliseconds(120)), partialStatus);
        Assert(partialAnalysis.IsPartial, "capped audio analysis should report partial scan scope");
        Assert(partialAnalysis.SampleCount < analysis.SampleCount, "capped audio analysis should avoid reading the whole file");
        Assert(partialAnalysis.AnalyzedDuration <= TimeSpan.FromMilliseconds(125), "capped audio analysis should stay inside the requested scan window");
        Assert(partialAnalysis.BrowserSummary.Contains("analyzed first", StringComparison.OrdinalIgnoreCase), "partial recording summaries should tell the user the scan is bounded");

        Assert(AudioFileAnalyzer.TryAnalyze(path, out var silentWindowAnalysis, out var silentWindowStatus, TimeSpan.FromMilliseconds(80)), silentWindowStatus);
        Assert(silentWindowAnalysis.IsPartial, "silent bounded windows should still report partial scan scope");
        Assert(silentWindowAnalysis.LeadingSilence <= TimeSpan.FromMilliseconds(85), "silent bounded windows should report silence for the scanned window, not the full file");

        var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
        Assert(windowCode.Contains("CreateAudioRecordingFileItem", StringComparison.Ordinal), "recording browser should create analyzed file rows");
        Assert(windowCode.Contains("MaximumAnalyzedRecordingRows", StringComparison.Ordinal), "recording browser should cap eager analysis rows");
        Assert(windowCode.Contains("EagerRecordingAnalysisDuration", StringComparison.Ordinal), "recording browser should cap eager analysis duration");
        Assert(windowCode.Contains("AudioFileAnalyzer.TryAnalyze(file.FullName, out var fileAnalysis, out _, EagerRecordingAnalysisDuration)", StringComparison.Ordinal), "recording browser should use bounded NAudio analysis for eager file rows");
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

static void WasapiExpertOutputSettingsArePersistedAndRouted()
{
    var lowLatency = WasapiOutputSettings.FromPersisted("LowLatency", exclusiveMode: false, customLatencyMilliseconds: null);
    Assert(lowLatency.Profile == WasapiOutputLatencyProfile.LowLatency, "WASAPI persisted profile should restore low-latency mode");
    Assert(lowLatency.EffectiveLatencyMilliseconds == WasapiOutputSettings.LowLatencyMilliseconds, "low-latency profile should use the low-latency buffer target");
    Assert(lowLatency.DisplayText.Contains("shared", StringComparison.Ordinal), "default WASAPI profile text should explain shared mode");

    var custom = WasapiOutputSettings.FromPersisted("Custom", exclusiveMode: true, customLatencyMilliseconds: 999);
    Assert(custom.Profile == WasapiOutputLatencyProfile.Custom, "WASAPI persisted profile should restore custom mode");
    Assert(custom.ExclusiveMode, "WASAPI persisted profile should restore exclusive mode");
    Assert(custom.CustomLatencyMilliseconds == WasapiOutputSettings.MaximumCustomLatencyMilliseconds, "custom WASAPI latency should be clamped for stability");
    Assert(custom.EffectiveLatencyMilliseconds == WasapiOutputSettings.MaximumCustomLatencyMilliseconds, "custom WASAPI latency should drive the effective buffer target");
    Assert(custom.DisplayText.Contains("exclusive", StringComparison.Ordinal), "custom WASAPI profile text should explain exclusive mode");

    using var service = new MicrophoneSpectrumService();
    service.ConfigureWasapiOutput(custom);
    Assert(service.WasapiOutputModeStatus.Contains("Custom exclusive", StringComparison.Ordinal), "audio service should surface the active WASAPI profile");

    var settingsType = typeof(EqualizerWindow).Assembly.GetType("JerichoDown.AppSettingsState")
        ?? throw new InvalidOperationException("app settings state type should be available");
    var settings = Activator.CreateInstance(settingsType)!;
    SetProperty(settings, "WasapiOutputProfile", WasapiOutputLatencyProfile.Custom.ToString());
    SetProperty(settings, "WasapiOutputExclusiveMode", true);
    SetProperty(settings, "WasapiOutputCustomLatencyMilliseconds", 190);
    var json = JsonSerializer.Serialize(settings, settingsType);
    var restored = JsonSerializer.Deserialize(json, settingsType)!;
    Assert((string)GetProperty(restored, "WasapiOutputProfile")! == WasapiOutputLatencyProfile.Custom.ToString(), "WASAPI profile should survive app-state roundtrip");
    Assert((bool)GetProperty(restored, "WasapiOutputExclusiveMode")!, "WASAPI exclusive mode should survive app-state roundtrip");
    Assert((int)GetProperty(restored, "WasapiOutputCustomLatencyMilliseconds")! == 190, "WASAPI custom latency should survive app-state roundtrip");

    var serviceSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    Assert(serviceSource.Contains("ConfigureWasapiOutput", StringComparison.Ordinal), "audio service should expose WASAPI output configuration");
    Assert(serviceSource.Contains("_wasapiOutputSettings.EffectiveLatencyMilliseconds", StringComparison.Ordinal), "WASAPI output should use the active latency profile");
    Assert(serviceSource.Contains("AudioClientShareMode.Exclusive", StringComparison.Ordinal), "WASAPI output should support expert exclusive mode");

    var windowCode = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    Assert(windowCode.Contains("WasapiOutputProfile = _wasapiOutputSettings.Profile.ToString()", StringComparison.Ordinal), "WASAPI profile should be persisted by the window state capture");
    Assert(windowCode.Contains("_spectrumService.ConfigureWasapiOutput(_wasapiOutputSettings)", StringComparison.Ordinal), "WASAPI settings should be applied before output routing starts");
    Assert(windowCode.Contains("WASAPI: {_spectrumService.WasapiOutputModeStatus}", StringComparison.Ordinal), "route status should show active WASAPI mode");

    var windowXaml = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml"));
    Assert(windowXaml.Contains("Advanced WASAPI", StringComparison.Ordinal), "advanced WASAPI controls should be present but tucked away");
    Assert(windowXaml.Contains("WasapiOutputProfileComboBox", StringComparison.Ordinal), "WASAPI profile dropdown should be wired");
    Assert(windowXaml.Contains("WasapiExclusiveModeCheckBox", StringComparison.Ordinal), "WASAPI exclusive-mode checkbox should be wired");
    Assert(windowXaml.Contains("WasapiCustomLatencySlider", StringComparison.Ordinal), "WASAPI custom latency slider should be wired");

    static object? GetProperty(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)!.GetValue(target);
    }

    static void SetProperty(object target, string propertyName, object? value)
    {
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);
    }
}

static void ProcessedOutputResamplesAsioFallbackFormats()
{
    var serviceSource = File.ReadAllText(FindRepoFile(Path.Combine("Audio", "MicrophoneSpectrumService.cs")));
    var asioOutputMethod = ExtractSourceBetween(
        serviceSource,
        "private bool TryStartAsioProcessedOutput(IWaveProvider provider, out IWavePlayer? player, out IWaveProvider? playbackProvider)",
        "private static bool TryStartAsioProcessedOutputProvider");
    Assert(asioOutputMethod.Contains("PreferredAsioSampleRates", StringComparison.Ordinal), "ASIO output should try known stable sample rates when the active rate fails");
    Assert(asioOutputMethod.Contains("CreateBestOutputResampler", StringComparison.Ordinal), "ASIO output should resample rather than failing immediately on a sample-rate mismatch");

    var resamplerMethod = typeof(MicrophoneSpectrumService).GetMethod("CreateWdlOutputResampler", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("processed output should expose a WDL output resampler helper");
    var source = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2))
    {
        ReadFully = true
    };
    var provider = (IWaveProvider)resamplerMethod.Invoke(null, [source, 48_000])!;
    Assert(provider.WaveFormat.SampleRate == 48_000, "WDL output resampler should advertise the fallback sample rate");
    Assert(provider.WaveFormat.Channels == 2, "WDL output resampler should preserve stereo routing");
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

static void KaraokeSampleReaderAcceptsExtendedFormats()
{
    foreach (var extension in new[] { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".aiff", ".aif" })
    {
        var path = Path.Combine(Path.GetTempPath(), "track" + extension);
        var isSupported = (bool)InvokeKaraokeTrackAudioReaderPrivateStatic("CanUseSampleReader", path);
        var isVisible = (bool)InvokeEqualizerWindowPrivateStatic("IsSupportedKaraokeTrackFile", path);

        Assert(isSupported, $"{extension} should use the NAudio sample reader path when the local codec can decode it");
        Assert(isVisible, $"{extension} should be visible in the karaoke track browser");
    }

    var m4pPath = Path.Combine(Path.GetTempPath(), "protected.m4p");
    Assert(!(bool)InvokeKaraokeTrackAudioReaderPrivateStatic("CanUseSampleReader", m4pPath), "protected Apple Music M4P should not use the sample reader path");
    Assert(!(bool)InvokeEqualizerWindowPrivateStatic("IsSupportedKaraokeTrackFile", m4pPath), "protected Apple Music M4P should stay hidden");
}

static void KaraokeSampleReaderFailuresUseMediaFallback()
{
    var codecError = new InvalidCastException("Unable to cast COM object of type 'System.__ComObject' to interface type 'NAudio.MediaFoundation.IMFSourceReader'.");
    var outputError = new IOException("The selected output device is unavailable.");

    Assert(
        (bool)InvokeEqualizerWindowPrivateStatic("ShouldTryKaraokeMediaFallbackAfterSampleReaderFailure", @"C:\Music\song.m4a", codecError),
        "MediaFoundation COM reader failures should fall back to Windows media playback");
    Assert(
        !(bool)InvokeEqualizerWindowPrivateStatic("ShouldTryKaraokeMediaFallbackAfterSampleReaderFailure", @"C:\Music\song.m4a", outputError),
        "plain output-device failures should not be hidden behind media fallback");
    Assert(
        !(bool)InvokeEqualizerWindowPrivateStatic("ShouldTryKaraokeMediaFallbackAfterSampleReaderFailure", @"C:\Music\protected.m4p", codecError),
        "unsupported protected tracks should not be routed into fallback playback");
}

static void KaraokePlaybackStoppedCodecFailuresUseMediaFallback()
{
    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var fallbackMethod = ExtractSourceBetween(
        windowSource,
        "    private bool StartKaraokeMediaFallbackPlayback(string path, TimeSpan? startPosition = null)",
        "    private void KaraokeMediaFallbackOpened");
    var completedMethod = ExtractSourceBetween(
        windowSource,
        "    private void HandleKaraokePlaybackCompleted(Exception? exception, bool wasStoppedByUser)",
        "    private void KaraokePlaybackPositionTimerTick");

    Assert(fallbackMethod.Contains("player.Position = clampedPosition", StringComparison.Ordinal), "media fallback should preserve the attempted playback position");
    Assert(completedMethod.Contains("stoppedSampleReaderPlayback", StringComparison.Ordinal), "playback stopped should know whether the NAudio sample reader path failed");
    Assert(completedMethod.Contains("ShouldTryKaraokeMediaFallbackAfterSampleReaderFailure(trackPath, exception)", StringComparison.Ordinal), "sample-reader playback exceptions should route through codec fallback detection");
    Assert(completedMethod.Contains("StartKaraokeMediaFallbackPlayback(trackPath, stoppedPosition)", StringComparison.Ordinal), "sample-reader playback exceptions should restart through Windows media fallback");
}

static void KaraokePlayRestartsAfterTrackEnd()
{
    var duration = TimeSpan.FromSeconds(180);

    var endedStart = (TimeSpan)InvokeEqualizerWindowPrivateStatic("ResolveKaraokePlaybackStartPosition", duration.TotalSeconds, duration);
    var nearEndStart = (TimeSpan)InvokeEqualizerWindowPrivateStatic("ResolveKaraokePlaybackStartPosition", duration.TotalSeconds - 0.1d, duration);
    var middleStart = (TimeSpan)InvokeEqualizerWindowPrivateStatic("ResolveKaraokePlaybackStartPosition", 42d, duration);

    Assert(endedStart == TimeSpan.Zero, "karaoke play should restart instead of resuming from the exact end");
    Assert(nearEndStart == TimeSpan.Zero, "karaoke play should restart when the transport is effectively at the end");
    Assert(Math.Abs((middleStart - TimeSpan.FromSeconds(42)).TotalMilliseconds) < 1d, "karaoke play should preserve normal mid-track resume positions");
}

static void KaraokeAddTracksLoadsIdleSelection()
{
    var windowSource = File.ReadAllText(FindRepoFile("EqualizerWindow.xaml.cs"));
    var addTracksMethod = ExtractSourceBetween(
        windowSource,
        "    private int AddKaraokeTracksToQueue(IEnumerable<string> paths, bool selectFirstAdded)",
        "    private void KaraokeQueueSelectionChanged");

    Assert(addTracksMethod.Contains("SetKaraokeTrack(added[0].Path, updateQueueSelection: true)", StringComparison.Ordinal), "adding a selected karaoke track while idle should load the highlighted playback target");
    Assert(!addTracksMethod.Contains("SelectQueuedKaraokeTrackWithoutLoading(added[0]);", StringComparison.Ordinal), "adding a karaoke track should not leave a highlighted item unloaded while idle");
}

static void AudioRecordingBrowserAcceptsExtendedPlaybackFormats()
{
    foreach (var extension in new[] { ".wav", ".mp3", ".m4a", ".aac", ".mp4", ".flac", ".aiff", ".aif", ".wma" })
    {
        var path = Path.Combine(Path.GetTempPath(), "recording" + extension);
        var isSupported = (bool)InvokeEqualizerWindowPrivateStatic("IsSupportedAudioRecordingFile", path);

        Assert(isSupported, $"{extension} should be accepted by the recording browser playback filter");
    }

    var protectedPath = Path.Combine(Path.GetTempPath(), "recording.m4p");
    Assert(!(bool)InvokeEqualizerWindowPrivateStatic("IsSupportedAudioRecordingFile", protectedPath), "M4P should not be accepted as a recording playback format");
}

static void AudioRecordingExporterSupportsCompressedTargets()
{
    var extensions = AudioRecordingExporter.ExportFormats.Select(format => format.Extension).ToArray();
    Assert(extensions.SequenceEqual(new[] { ".mp3", ".mp4", ".wma" }), "compressed export targets should be MP3, AAC-in-MP4, and WMA");
    Assert(AudioRecordingExporter.TryGetFormatForExtension("mix.aac", out var aacInfo), "AAC files should map to the AAC exporter");
    Assert(aacInfo.Format == AudioRecordingExportFormat.Aac, "AAC extension should use the AAC Media Foundation encoder");
    Assert(!AudioRecordingExporter.TryGetFormatForExtension("protected.m4p", out _), "protected Apple Music M4P should not be an export target");

    var defaultAacPath = AudioRecordingExporter.GetDefaultExportPath(@"C:\Sessions\mix_001.wav", AudioRecordingExportFormat.Aac);
    Assert(defaultAacPath.EndsWith("mix_001_export.mp4", StringComparison.OrdinalIgnoreCase), "AAC export should default to the MP4 container extension");

    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var sourcePath = Path.Combine(folder, "source.wav");
        using (var writer = new WaveFileWriter(sourcePath, WaveFormat.CreateIeeeFloatWaveFormat(44_100, 1)))
        {
            var samples = Enumerable.Range(0, 2_205)
                .Select(i => (float)(Math.Sin(i * Math.PI * 2d * 440d / 44_100d) * 0.15d))
                .ToArray();
            writer.WriteSamples(samples, 0, samples.Length);
        }

        AudioRecordingExportFormatInfo? availableFormat = AudioRecordingExporter.ExportFormats.FirstOrDefault(format =>
            AudioRecordingExporter.IsEncoderAvailable(format.Format, new WaveFormat(44_100, 16, 1)));
        if (availableFormat is null)
        {
            return;
        }

        var targetPath = Path.Combine(folder, "source_export" + availableFormat.Extension);
        AudioRecordingExporter.Export(sourcePath, targetPath, availableFormat.Format);
        Assert(File.Exists(sourcePath), "export should leave the source recording intact");
        Assert(File.Exists(targetPath), "export should create the compressed target file when the encoder is available");
        Assert(new FileInfo(targetPath).Length > 0, "compressed export should not be empty");
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

static void KaraokeBrowserDfsHidesM4pTracks()
{
    var folder = Path.Combine(Path.GetTempPath(), "JerichoDown.Tests", Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(folder, "iTunes", "iTunes Media", "Music", "Artist", "Album");
    Directory.CreateDirectory(nested);
    try
    {
        var m4pPath = Path.Combine(nested, "01 Protected Song.m4p");
        var ignoredPath = Path.Combine(nested, "cover.jpg");
        File.WriteAllBytes(m4pPath, CreateMinimalM4aWithDuration(44100, 44100));
        File.WriteAllBytes(ignoredPath, [1, 2, 3, 4]);

        var isSupported = (bool)InvokeEqualizerWindowPrivateStatic("IsSupportedKaraokeTrackFile", m4pPath);
        var files = ((IEnumerable<string>)InvokeEqualizerWindowPrivateStatic("EnumerateKaraokeTrackFiles", folder)).ToList();

        Assert(!isSupported, "M4P tracks should stay hidden because protected Apple Music files are not reliably playable");
        Assert(files.Count == 0, "DFS should hide unsupported nested M4P tracks");
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

static object InvokeKaraokeTrackAudioReaderPrivateStatic(string methodName, params object?[] args)
{
    var readerType = typeof(EqualizerWindow).GetNestedType("KaraokeTrackAudioReader", BindingFlags.NonPublic);
    if (readerType is null)
    {
        throw new InvalidOperationException("KaraokeTrackAudioReader type was not found");
    }

    var method = readerType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException($"KaraokeTrackAudioReader.{methodName} was not found");
    }

    return method.Invoke(null, args) ?? throw new InvalidOperationException($"{methodName} returned null");
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

static string ExtractSourceBetween(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, $"Could not find source marker: {startMarker}");
    var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    Assert(end > start, $"Could not find source end marker: {endMarker}");
    return source[start..end];
}

static string FindRepoFile(string relativePath)
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    throw new FileNotFoundException($"Could not find {relativePath} from {Environment.CurrentDirectory}");
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

static void SetPrivateField<T>(object target, string name, T value)
{
    var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    if (field is null)
    {
        throw new InvalidOperationException($"{target.GetType().Name}.{name} was not found");
    }

    field.SetValue(target, value);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
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

static float[] ReadWaveFloatSamples(WaveFileReader reader)
{
    var bytes = new byte[reader.Length];
    var read = reader.Read(bytes, 0, bytes.Length);
    return MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, read)).ToArray();
}

static VoiceProcessorSettings CreateTransparentVoiceSettings()
{
    return new VoiceProcessorSettings
    {
        InputTrimDb = 0,
        HighPassEnabled = false,
        LowPassEnabled = false,
        HumRemovalEnabled = false,
        NotchFilterEnabled = false,
        ParametricEqEnabled = false,
        ShelfEqEnabled = false,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        BreathReducerEnabled = false,
        DeEsserEnabled = false,
        PresenceEnhancerEnabled = false,
        SaturationEnabled = false,
        MakeupGainDb = 0,
        LimiterEnabled = false,
        LimiterSoftClipEnabled = false,
        LimiterLookaheadEnabled = false
    };
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

static float[] ProcessShelfEqTestTone(float[] samples, bool enabled)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        HumRemovalEnabled = false,
        NotchFilterEnabled = false,
        ParametricEqEnabled = false,
        ShelfEqEnabled = enabled,
        LowShelfFrequencyHz = 180,
        LowShelfGainDb = 6,
        HighShelfFrequencyHz = 7_500,
        HighShelfGainDb = -7,
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

static float[] ProcessNAudioBiQuadTestTone(float[] samples, Action<VoiceProcessorSettings> configure)
{
    var settings = CreateTransparentVoiceSettings();
    configure(settings);
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessNAudioPitchShiftTestTone(float[] samples, Action<VoiceProcessorSettings> configure)
{
    var settings = CreateTransparentVoiceSettings();
    configure(settings);
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessNAudioConvolutionTestTone(float[] samples, Action<VoiceProcessorSettings> configure)
{
    var settings = CreateTransparentVoiceSettings();
    configure(settings);
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessNAudioEnvelopeTestTone(float[] samples, Action<VoiceProcessorSettings> configure)
{
    var settings = CreateTransparentVoiceSettings();
    configure(settings);
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessNAudioDmoTestTone(float[] samples, Action<VoiceProcessorSettings> configure)
{
    var settings = CreateTransparentVoiceSettings();
    configure(settings);
    var processor = new VoiceSampleProcessor(settings, sampleRate: 48_000);
    return processor.Process(samples);
}

static float[] ProcessBreathReducerTestTone(float[] samples, bool enabled)
{
    var settings = new VoiceProcessorSettings
    {
        HighPassEnabled = false,
        HumRemovalEnabled = false,
        NotchFilterEnabled = false,
        ParametricEqEnabled = false,
        ShelfEqEnabled = false,
        LowPassEnabled = false,
        DePopperEnabled = false,
        NoiseGateEnabled = false,
        ExpanderEnabled = false,
        NoiseSuppressionEnabled = false,
        EchoReducerEnabled = false,
        CompressorEnabled = false,
        BreathReducerEnabled = enabled,
        BreathReducerAmountDb = 18,
        BreathReducerSensitivity = 10,
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

static double CalculateToneMagnitude(
    IReadOnlyList<float> samples,
    int sampleRate,
    double frequencyHz,
    int startIndex,
    int? windowLength = null)
{
    var start = Math.Clamp(startIndex, 0, samples.Count);
    var end = windowLength.HasValue
        ? Math.Min(samples.Count, start + Math.Max(0, windowLength.Value))
        : samples.Count;
    var sine = 0d;
    var cosine = 0d;
    var count = 0;
    for (var i = start; i < end; i++)
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

static double CalculateWindowRms(IReadOnlyList<float> samples, int startIndex, int length)
{
    var start = Math.Clamp(startIndex, 0, samples.Count);
    var end = Math.Min(samples.Count, start + Math.Max(0, length));
    var sum = 0d;
    var count = 0;
    for (var i = start; i < end; i++)
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

sealed class FakeWaveIn : IWaveIn
{
    public FakeWaveIn(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; set; }

    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add { }
        remove { }
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add { }
        remove { }
    }

    public void StartRecording()
    {
    }

    public void StopRecording()
    {
    }

    public void Dispose()
    {
    }
}

sealed class ArraySampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;

    public ArraySampleProvider(IEnumerable<float> samples, WaveFormat waveFormat)
    {
        _samples = samples.ToArray();
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = Math.Min(count, _samples.Length - _position);
        if (read <= 0)
        {
            return 0;
        }

        Array.Copy(_samples, _position, buffer, offset, read);
        _position += read;
        return read;
    }
}
