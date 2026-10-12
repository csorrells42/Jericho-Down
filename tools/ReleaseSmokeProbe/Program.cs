using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JerichoDown;
using JerichoDown.Modules.Audio.Devices;
using JerichoDown.Modules.Audio.Live;
using JerichoDown.Modules.Audio.Capture;
using JerichoDown.Modules.Midi;
using JerichoDown.Modules.SessionPlayback;
using NAudio.Wave;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var profile = Path.Combine(Path.GetTempPath(), "JerichoDown-Smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        AppContext.SetData("JerichoDown.SettingsFolder", profile);
        Console.WriteLine($"Isolated smoke profile: {profile}");
        var tone = AudioInputDevice.CreateStereoTestTone();
        File.WriteAllText(Path.Combine(profile, "app-state.json"), JsonSerializer.Serialize(new
        {
            UpdatedAt = DateTimeOffset.Now,
            CameraEnabled = false,
            ProcessedOutputEnabled = false,
            SelectedMicChannelNumber = 1,
            SystemAudioLoopbackDefaultMuteApplied = true,
            AudioRecordingFolder = profile,
            KaraokeRecordingFolder = profile,
            OutputFolder = profile,
            KaraokeBrowserFolder = profile,
            MicChannels = Enumerable.Range(1, 11).Select(number => new
            {
                ChannelNumber = number,
                MicrophoneName = number <= 2 ? tone.Name : null,
                MicrophoneEndpointId = number <= 2 ? tone.EndpointId : null,
                InputChannelMode = "StereoPair",
                IsEnabled = number <= 2,
                IsMuted = number != 1,
                VolumePercent = 100
            })
        }));

        using var timeout = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(90), timeout.Token); }
            catch (OperationCanceledException) { return; }
            Console.Error.WriteLine("FAIL Smoke probe exceeded 90 seconds; terminating only the probe process.");
            Environment.Exit(2);
        });

        if (args.Contains("--audio-hardware", StringComparer.Ordinal))
        {
            var result = VerifyAudioHardware(args.Contains("--asio", StringComparer.Ordinal));
            timeout.Cancel();
            return result;
        }

        var errors = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        var exitCode = 1;
        var app = new App();
        var dispatcherErrors = new List<Exception>();
        app.DispatcherUnhandledException += (_, e) => dispatcherErrors.Add(e.Exception);
        app.Startup += async (_, _) =>
        {
            EqualizerWindow? window = null;
            try
            {
                window = new EqualizerWindow { Width = 1400, Height = 900, ShowActivated = false };
                app.MainWindow = window;
                window.Show();
                await VerifyAsync(window, profile);
                if (args.Contains("--video", StringComparer.Ordinal))
                    await VerifyPodcastAsync(window, profile);
                if (args.Contains("--visual", StringComparer.Ordinal))
                {
                    var tabs = Control<TabControl>(window, "MainTabControl");
                    tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Mixing"));
                    Console.WriteLine("Visual inspection window is ready for 60 seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(60));
                }
                if (errors.Messages.Count > 0)
                    throw new InvalidOperationException("WPF binding errors: " + string.Join(Environment.NewLine, errors.Messages.Distinct()));
                if (dispatcherErrors.Count > 0)
                    throw new AggregateException("Unhandled WPF errors were caught by the app.", dispatcherErrors);
                var service = Field<MicrophoneSpectrumService>(window, "_spectrumService");
                Invoke(window, "BeginKaraokeLyricWork");
                var pendingLyrics = Field<CancellationTokenSource>(window, "_karaokeLyricsCancellation");
                window.Close();
                if (!pendingLyrics.IsCancellationRequested) throw new InvalidOperationException("Closing left lyric work active.");
                Invoke(window, "CompleteKaraokeLyricWork", pendingLyrics);
                if (service.IsRunning) throw new InvalidOperationException("Capture remained active after closing the window.");
                Console.WriteLine("PASS Window closes and releases capture");
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL {ex}");
                window?.Close();
                app.Shutdown(1);
            }
        };
        app.Run();
        timeout.Cancel();
        using var run = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "run-state.json")));
        if (run.RootElement.GetProperty("CleanShutdownAt").ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("The app did not persist a clean shutdown marker.");
        Console.WriteLine("PASS Clean shutdown marker persisted");
        return exitCode;
    }

    private static async Task VerifyAsync(EqualizerWindow window, string profile)
    {
        await Task.Delay(500);
        var tabs = Control<TabControl>(window, "MainTabControl");
        if (tabs.Items.Count != 5) throw new InvalidOperationException("Unexpected main tab count.");
        var midiTab = Control<TabItem>(window, "MidiTabItem");
        var midiMenu = Control<MenuItem>(window, "EnableMidiMenuItem");
        if (midiTab.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("MIDI should be opt-in for a new profile.");
        midiMenu.SetCurrentValue(MenuItem.IsCheckedProperty, true);
        if (midiTab.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Enable MIDI did not reveal the tab.");
        Console.WriteLine("PASS MIDI opt-in menu reveals its tab");
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var tab in tabs.Items.Cast<TabItem>())
            {
                var watch = Stopwatch.StartNew();
                tabs.SelectedItem = tab;
                window.UpdateLayout();
                await Task.Delay(150);
                if (watch.Elapsed > TimeSpan.FromSeconds(5))
                    throw new InvalidOperationException($"Tab {tab.Header} stalled for {watch.Elapsed}.");
                Console.WriteLine($"PASS Tab {tab.Header} opens ({watch.ElapsedMilliseconds} ms)");
            }
        }

        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Mixing"));
        window.UpdateLayout();
        await Task.Delay(100);
        var strips = Control<ItemsControl>(window, "MixingChannelsItemsControl");
        var selectedPanel = Control<StackPanel>(window, "SelectedMixerInputPanel");
        foreach (var channel in strips.Items)
        {
            var presenter = (DependencyObject)strips.ItemContainerGenerator.ContainerFromItem(channel);
            var strip = Descendants<Border>(presenter).First(border => ReferenceEquals(border.DataContext, channel));
            var watch = Stopwatch.StartNew();
            strip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
            });
            await Task.Delay(50);
            if (!ReferenceEquals(selectedPanel.DataContext, channel))
                throw new InvalidOperationException("Mixer selection did not update its input editor.");
            var mute = Descendants<CheckBox>(strip).Single(box => Equals(box.Content, "Mute"));
            var original = mute.IsChecked;
            mute.SetCurrentValue(ToggleButton.IsCheckedProperty, !original);
            mute.SetCurrentValue(ToggleButton.IsCheckedProperty, original);
            if (watch.Elapsed > TimeSpan.FromSeconds(2))
                throw new InvalidOperationException("Mixer selection or mute stalled.");
        }
        Console.WriteLine($"PASS Selection and mute on all {strips.Items.Count} mixer strips");

        // Returning from the computer-audio strip must select a real EQ input.
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Mic / DSP"));
        await Task.Delay(300);
        var inputPicker = Control<ComboBox>(window, "MicrophoneComboBox");
        if (inputPicker.Items.Cast<AudioInputDevice>().Any(device => device.IsSystemAudioLoopback || device.IsProcessLoopback))
            throw new InvalidOperationException("Loopback appeared in the EQ input picker.");
        Console.WriteLine("PASS EQ picker excludes loopback after mixer navigation");

        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Mixing"));
        await Task.Delay(300);
        var service = Field<MicrophoneSpectrumService>(window, "_spectrumService");
        Click(window, "AudioRecordButton");
        await Task.Delay(300);
        if (!service.IsProcessedAudioRecording) throw new InvalidOperationException("Record did not start.");
        Click(window, "AudioPauseButton");
        if (!service.IsProcessedAudioRecordingPaused) throw new InvalidOperationException("Pause did not pause recording.");
        Click(window, "AudioPauseButton");
        await Task.Delay(200);
        Click(window, "AudioStopButton");
        if (service.IsProcessedAudioRecording) throw new InvalidOperationException("Stop did not finalize recording.");
        var path = Directory.GetFiles(profile, "*.wav").Single();
        using (var reader = new AudioFileReader(path))
        {
            var samples = new float[48000];
            var count = reader.Read(samples, 0, samples.Length);
            if (count < 4000 || !samples.Take(count).Any(sample => Math.Abs(sample) > 0.001f))
                throw new InvalidOperationException("Recorded WAV is empty or silent.");
        }
        Console.WriteLine("PASS Record, pause, resume, stop and reopen audible WAV");

        var timestamp = DateTime.Now;
        var createKaraokePath = typeof(EqualizerWindow).GetMethod("CreateKaraokeVocalRecordingPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstPath = (string)createKaraokePath.Invoke(window, [timestamp])!;
        File.WriteAllBytes(firstPath, [1, 2, 3]);
        var secondPath = (string)createKaraokePath.Invoke(window, [timestamp])!;
        if (firstPath == secondPath) throw new InvalidOperationException("Rapid karaoke recordings reuse the same filename.");
        File.Delete(firstPath);
        Console.WriteLine("PASS Same-second karaoke recordings choose separate files");

        var backingPath = Path.Combine(profile, "smoke-backing.wav");
        using (var writer = new WaveFileWriter(backingPath, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
        {
            var samples = Enumerable.Range(0, 48000 * 8)
                .Select(i => (float)(0.03 * Math.Sin(i / 2 * 2 * Math.PI * 220 / 48000))).ToArray();
            writer.WriteSamples(samples, 0, samples.Length);
        }
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Karaoke"));
        typeof(EqualizerWindow).GetMethod("SetKaraokeTrack", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [backingPath, false]);
        Click(window, "KaraokeRecordVocalButton");
        await Task.Delay(200);
        if (!service.IsProcessedAudioRecording || !Field<bool>(window, "_isKaraokeTrackPlaying"))
            throw new InvalidOperationException("Karaoke did not start both backing playback and vocal recording.");
        Click(window, "KaraokePauseVocalButton");
        if (!service.IsProcessedAudioRecordingPaused || Field<bool>(window, "_isKaraokeTrackPlaying"))
            throw new InvalidOperationException("Karaoke did not pause playback and recording together.");
        Click(window, "KaraokePauseVocalButton");
        await Task.Delay(200);
        Click(window, "KaraokeStopVocalButton");
        if (service.IsProcessedAudioRecording || Field<bool>(window, "_isKaraokeTrackPlaying"))
            throw new InvalidOperationException("Karaoke stop left playback or recording running.");
        var vocal = Directory.GetFiles(profile, "karaokeRecording_*.wav").Single();
        using (var reader = new AudioFileReader(vocal))
        {
            if (reader.TotalTime.TotalSeconds < 0.1)
                throw new InvalidOperationException("Karaoke vocal recording did not finalize.");
        }
        Console.WriteLine("PASS Karaoke backing playback and vocal record/pause/resume/stop chain");

        Invoke(window, "BeginKaraokeLyricWork");
        var staleLyrics = Field<CancellationTokenSource>(window, "_karaokeLyricsCancellation");
        Control<TextBox>(window, "KaraokeLyricsTextBox").Text = "Edited lyrics must remain intact";
        if (!staleLyrics.IsCancellationRequested) throw new InvalidOperationException("Editing lyrics did not invalidate pending analysis.");
        Invoke(window, "CompleteKaraokeLyricWork", staleLyrics);
        var nextBackingPath = Path.Combine(profile, "next-backing.wav");
        File.Copy(backingPath, nextBackingPath);
        Invoke(window, "BeginKaraokeLyricWork");
        staleLyrics = Field<CancellationTokenSource>(window, "_karaokeLyricsCancellation");
        Invoke(window, "SetKaraokeTrack", nextBackingPath, false);
        if (!staleLyrics.IsCancellationRequested) throw new InvalidOperationException("Changing tracks did not invalidate pending lyrics.");
        var isCurrent = (bool)typeof(EqualizerWindow).GetMethod("IsKaraokeLyricWorkCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [staleLyrics])!;
        if (isCurrent) throw new InvalidOperationException("Stale lyric results could still overwrite the new track.");
        Invoke(window, "CompleteKaraokeLyricWork", staleLyrics);
        Console.WriteLine("PASS Lyric edits and track changes invalidate pending analysis");

        service.Stop();
        for (var attempt = 0; attempt < 3; attempt++)
            Invoke(window, "RegisterAudioStreamRestartFailure", new IOException("Test device unavailable"), "Audio stream refresh failed");
        typeof(EqualizerWindow).GetField("_nextAudioStreamRestartAttemptUtc", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, DateTime.MinValue);
        Invoke(window, "AudioDeviceFormatTimerTick", null, EventArgs.Empty);
        await Task.Delay(200);
        if (service.IsRunning || Field<bool>(window, "_isRestartingAudioStream")
            || !Control<TextBlock>(window, "StatusText").Text.Contains("Automatic retries paused", StringComparison.Ordinal))
            throw new InvalidOperationException($"Repeated device failures did not stop automatic reopen attempts: running={service.IsRunning}, restarting={Field<bool>(window, "_isRestartingAudioStream")}, failures={Field<int>(window, "_audioStreamRestartFailureCount")}, status={Control<TextBlock>(window, "StatusText").Text}");
        Invoke(window, "RefreshAudioDevicesMenuClicked", window, new RoutedEventArgs());
        if (Field<int>(window, "_audioStreamRestartFailureCount") != 0)
            throw new InvalidOperationException("Explicit device refresh did not release the retry limit.");
        service.Start(AudioInputDevice.CreateStereoTestTone());
        Console.WriteLine("PASS Failed audio retries stop and explicit device refresh re-enables recovery");
    }

    private static int VerifyAudioHardware(bool includeAsio)
    {
        var failures = 0;
        var devices = MicrophoneSpectrumService.GetInputDevices()
            .Where(device => !device.IsStereoTestTone && !device.IsSystemAudioLoopback && !device.IsProcessLoopback
                && (includeAsio || !device.IsAsio));
        foreach (var device in devices)
        {
            using var service = new MicrophoneSpectrumService();
            using var frameReceived = new ManualResetEventSlim();
            service.SpectrumAvailable += (_, _) => frameReceived.Set();
            try
            {
                service.Start(device, null, InputChannelMode.StereoPair);
                if (!frameReceived.Wait(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("No captured audio frames arrived within three seconds.");
                Console.WriteLine($"PASS Input {device.Name}: {service.ActiveInputFormatStatus}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UNAVAILABLE Input {device.Name}: {ex.Message}");
                if (!device.IsAsio) failures++;
            }
            service.Stop();
        }

        var output = MicrophoneSpectrumService.GetOutputDevices().FirstOrDefault(device => !device.IsAsio);
        if (output is not null)
        {
            using var service = new MicrophoneSpectrumService();
            try
            {
                service.Start(AudioInputDevice.CreateStereoTestTone());
                service.ConfigureProcessedOutput(true, output);
                Thread.Sleep(500);
                if (!service.IsProcessedOutputEnabled) throw new InvalidOperationException("Output route did not remain enabled.");
                Console.WriteLine($"PASS Output {output.Name}: {service.ActualProcessedOutputFormatStatus}");
                using (var systemAudio = new MicrophoneSpectrumService())
                using (var received = new ManualResetEventSlim())
                {
                    systemAudio.SpectrumAvailable += (_, frame) =>
                    {
                        if (frame.RawPeakLevel > .0001) received.Set();
                    };
                    systemAudio.Start(AudioInputDevice.CreateSystemAudioLoopback());
                    if (!received.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("Computer loopback received no generated audio.");
                    systemAudio.Stop();
                    Console.WriteLine("PASS Computer loopback receives generated speaker audio and stops");
                }
                using (var loopback = new ProcessLoopbackCapture(Environment.ProcessId))
                using (var received = new ManualResetEventSlim())
                using (var stopped = new ManualResetEventSlim())
                {
                    Exception? captureError = null;
                    var disposeOnData = false;
                    loopback.RecordingStopped += (_, e) => { captureError = e.Exception; stopped.Set(); };
                    loopback.DataAvailable += (_, e) =>
                    {
                        if (e.BytesRecorded > 0 && e.Buffer.Take(e.BytesRecorded).Any(value => value != 0)) received.Set();
                        if (disposeOnData) loopback.Dispose();
                    };
                    loopback.StartRecording();
                    if (!received.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("Process loopback received no generated audio.");
                    loopback.StopRecording();
                    if (captureError is not null) throw captureError;
                    Console.WriteLine("PASS Per-app loopback captures generated audio and stops");
                    stopped.Reset();
                    disposeOnData = true;
                    loopback.StartRecording();
                    if (!stopped.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("Loopback did not stop after callback disposal.");
                    if (captureError is not null) throw captureError;
                    Console.WriteLine("PASS Per-app loopback reopens and safely disposes inside its data callback");
                }
                service.ConfigureProcessedOutput(false, output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL Output {output.Name}: {ex.Message}");
                failures++;
            }
        }
        else
        {
            Console.WriteLine("UNAVAILABLE No Windows output device.");
            failures++;
        }

        var midiInputs = MidiDeviceCatalog.GetInputDevices();
        var midiOutputs = MidiDeviceCatalog.GetOutputDevices();
        Console.WriteLine($"MIDI devices: {midiInputs.Count} inputs, {midiOutputs.Count} outputs.");
        var softwareSynth = midiOutputs.FirstOrDefault(device => device.ProductName == "Microsoft GS Wavetable Synth");
        if (softwareSynth is not null)
        {
            try
            {
                using var midi = new MidiOutputPort();
                midi.Open(softwareSynth);
                midi.SendBankSelect(1, 0);
                midi.SendPatchChange(1, 0);
                midi.SendControlChange(1, 7, 25);
                midi.SendPitchWheel(1, 8192);
                midi.SendNoteOn(1, 60, 25);
                Thread.Sleep(100);
                midi.SendNoteOff(1, 60, 0);
                midi.SendAllNotesOff();
                midi.SendResetAllControllers();
                midi.Reset();
                midi.Close();
                Console.WriteLine("PASS Windows software MIDI output accepts bank, patch, control, pitch, notes and reset");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL Windows software MIDI output: {ex.Message}");
                failures++;
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static async Task VerifyPodcastAsync(EqualizerWindow window, string profile)
    {
        var tabs = Control<TabControl>(window, "MainTabControl");
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Podcast"));
        Control<ToggleButton>(window, "CameraEnabledToggle").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        await Task.Delay(2500);
        Click(window, "StartRecordingButton");
        await Task.Delay(1200);
        Click(window, "PauseRecordingButton");
        await Task.Delay(150);
        Click(window, "PauseRecordingButton");
        await Task.Delay(500);
        Invoke(window, "VideoSegmentRotationTimerTick", null, EventArgs.Empty);
        await Task.Delay(1000);
        Click(window, "StopRecordingButton");
        var folder = Directory.GetDirectories(profile, "Podcast_*").Single();
        foreach (var name in new[] { "video_001.mp4", "mix_001.wav", "raw_backup_001.wav", "video_001.json", "video_002.mp4", "mix_002.wav", "raw_backup_002.wav", "video_002.json", "session.json" })
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path) || new FileInfo(path).Length < 100)
                throw new InvalidOperationException($"Podcast session did not produce {name}.");
            if (name.EndsWith(".wav", StringComparison.Ordinal))
            {
                using var reader = new AudioFileReader(path);
                var samples = new float[48000];
                var count = reader.Read(samples, 0, samples.Length);
                if (count < 4000 || !samples.Take(count).Any(sample => Math.Abs(sample) > 0.001f))
                    throw new InvalidOperationException($"Podcast {name} has no audible samples.");
            }
        }
        Control<ToggleButton>(window, "CameraEnabledToggle").SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        for (var set = 1; set <= 2; set++)
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, $"video_{set:000}.json")));
            using var audio = new AudioFileReader(Path.Combine(folder, $"mix_{set:000}.wav"));
            var duration = TimeSpan.Parse(metadata.RootElement.GetProperty("duration").GetString()!);
            if (Math.Abs(duration.TotalSeconds - audio.TotalTime.TotalSeconds) > 1.1)
                throw new InvalidOperationException("Segment metadata contains cumulative rather than per-segment duration.");
        }
        Console.WriteLine("PASS Podcast video, processed mix, raw backup, pause/resume, segment rotation and metadata");

        using (var playback = new MediaFoundationFilePlaybackService())
        {
            var frames = 0;
            playback.FrameAvailable += (_, _) => Interlocked.Increment(ref frames);
            var path = Path.Combine(folder, "video_001.mp4");
            if (!playback.Start(path)) throw new InvalidOperationException("Recorded video could not open for playback.");
            await Task.Delay(300);
            playback.Pause();
            playback.Seek(TimeSpan.Zero);
            playback.Play();
            await Task.Delay(300);
            playback.Stop();
            if (frames == 0 || playback.IsRunning) throw new InvalidOperationException("Playback did not render frames and stop.");
            if (!playback.Start(path)) throw new InvalidOperationException("Playback could not reopen after stop.");
            await Task.Delay(150);
        }
        Console.WriteLine("PASS Recorded video playback, pause/seek/resume, stop and reopen");

        using (var playback = new MediaFoundationFilePlaybackService())
        {
            var path = Path.Combine(folder, "video_001.mp4");
            var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => playback.Start(path))));
            if (starts.Any(started => !started)) throw new InvalidOperationException("Overlapping playback starts did not serialize cleanly.");
            await Task.WhenAll(Task.Run(playback.Stop), Task.Run(playback.Dispose));
            if (playback.IsRunning) throw new InvalidOperationException("Concurrent playback shutdown left a worker active.");
            try
            {
                playback.Start(path);
                throw new InvalidOperationException("Disposed playback reopened.");
            }
            catch (ObjectDisposedException) { }
        }
        Console.WriteLine("PASS Concurrent playback starts, stop/dispose and disposed reopen guard");

        Click(window, "StartRecordingButton");
        await Task.Delay(300);
        Field<MicrophoneSpectrumService>(window, "_spectrumService").Stop();
        await Task.Delay(500);
        if (Field<bool>(window, "_isRecordingSession"))
            throw new InvalidOperationException("Podcast UI kept recording after capture stopped.");
        if (!Control<TextBlock>(window, "RecordingStatusText").Text.Contains("Session ended", StringComparison.Ordinal))
            throw new InvalidOperationException("Podcast recording did not explain the interrupted stream.");
        Console.WriteLine("PASS Capture interruption finalizes podcast session and updates the UI");
    }

    private static T Control<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        (T)(root.FindName(name) ?? throw new InvalidOperationException($"Missing control {name}."));
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Click(FrameworkElement root, string name) =>
        Control<Button>(root, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Invoke(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (message is not null) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
