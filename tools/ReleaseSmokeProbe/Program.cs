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
using JerichoDown.Audio;
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
            var result = VerifyAudioHardware();
            timeout.Cancel();
            return result;
        }

        var errors = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        var exitCode = 1;
        var app = new App();
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
                if (errors.Messages.Count > 0)
                    throw new InvalidOperationException("WPF binding errors: " + string.Join(Environment.NewLine, errors.Messages.Distinct()));
                var service = Field<MicrophoneSpectrumService>(window, "_spectrumService");
                window.Close();
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
    }

    private static int VerifyAudioHardware()
    {
        var failures = 0;
        var devices = MicrophoneSpectrumService.GetInputDevices()
            .Where(device => !device.IsStereoTestTone && !device.IsSystemAudioLoopback && !device.IsProcessLoopback);
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
        Click(window, "StopRecordingButton");
        var folder = Directory.GetDirectories(profile, "Podcast_*").Single();
        foreach (var name in new[] { "video_001.mp4", "mix_001.wav", "raw_backup_001.wav", "session.json" })
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
        Console.WriteLine("PASS Podcast video, processed mix, raw backup, pause/resume and metadata");

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
