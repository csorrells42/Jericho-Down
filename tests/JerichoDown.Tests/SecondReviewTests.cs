using System.Diagnostics;
using System.Reflection;
using JerichoDown.Modules.Audio.Dsp;
using JerichoDown.Modules.Mixer;
using JerichoDown.Modules.Midi;
using JerichoDown.Modules.SessionPlayback;
using NAudio.Midi;

internal static class SecondReviewTests
{
    public static void EveryDspToggleSurvivesRateAndBypassChanges()
    {
        var toggles = typeof(VoiceProcessorSettings).GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite).ToArray();
        var cases = 0;
        foreach (var rate in new[] { 8000, 11025, 16000, 44100, 48000, 96000, 192000 })
        {
            var input = Enumerable.Range(0, 4096).Select(i => (float)(0.2 * Math.Sin(2 * Math.PI * 440 * i / rate))).ToArray();
            input[10] = float.NaN;
            input[20] = float.PositiveInfinity;
            foreach (var enabled in toggles)
            {
                var settings = new VoiceProcessorSettings { NAudioPitchShiftSemitones = 7, ParametricEqGainDb = 6,
                    LowShelfGainDb = 6, HighShelfGainDb = -6, NAudioDmoParamEqGainDb = 6 };
                foreach (var toggle in toggles) toggle.SetValue(settings, false);
                enabled.SetValue(settings, true);
                if (enabled.Name.StartsWith("Limiter", StringComparison.Ordinal)) settings.LimiterEnabled = true;
                var processor = new VoiceSampleProcessor(settings, rate);
                foreach (var active in new[] { true, false, true })
                {
                    enabled.SetValue(settings, active);
                    var output = processor.Process(input);
                    if (output.Length != input.Length || output.Any(s => !float.IsFinite(s) || Math.Abs(s) > 1.00001f))
                        throw new InvalidOperationException($"{enabled.Name} at {rate} Hz violated final output safety.");
                }
                foreach (var toggle in toggles) toggle.SetValue(settings, false);
                processor.Process(new float[64]);
                cases++;
            }
        }
        Console.WriteLine($"  DSP safety matrix: {cases} effect/rate cases, each enabled, bypassed and re-enabled.");
    }

    public static void PlaybackCanStopFromItsOwnWorker()
    {
        using var service = new MediaFoundationFilePlaybackService();
        using var ready = new ManualResetEventSlim();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var worker = Task.Run(() =>
        {
            ready.Wait();
            var watch = Stopwatch.StartNew();
            service.Stop();
            if (watch.Elapsed > TimeSpan.FromMilliseconds(500))
                throw new InvalidOperationException("Playback waited for its own callback to finish.");
        });
        typeof(MediaFoundationFilePlaybackService).GetField("_playbackTask", flags)!.SetValue(service, worker);
        typeof(MediaFoundationFilePlaybackService).GetField("_cancellation", flags)!.SetValue(service, new CancellationTokenSource());
        ready.Set();
        if (!worker.Wait(3000)) throw new InvalidOperationException("Playback stop did not finish.");
    }

    public static void PitchShiftCanChangeFrameGeometryLive()
    {
        var settings = new VoiceProcessorSettings { NAudioPitchShiftEnabled = true, NAudioPitchShiftSemitones = 7,
            NAudioPitchShiftMix = 1, NAudioPitchShiftFftSize = 256, NAudioPitchShiftOversampling = 4 };
        var processor = new NAudioPitchShiftProcessor(48000);
        foreach (var (frame, overlap) in new[] { (256, 4), (4096, 4), (512, 2), (512, 32), (1024, 8) })
        {
            settings.NAudioPitchShiftFftSize = frame;
            settings.NAudioPitchShiftOversampling = overlap;
            processor.UpdateFromSettings(settings);
            var samples = Enumerable.Range(0, 997).Select(i => (float)(0.1 * Math.Sin(i * .1))).ToArray();
            processor.Transform(samples);
            if (samples.Any(sample => !float.IsFinite(sample))) throw new InvalidOperationException("Pitch geometry change produced invalid output.");
        }
    }

    public static void PitchShiftDoesNotReplayBypassedAudio()
    {
        foreach (var bypass in new[] { "disabled", "dry", "unity" })
        {
            var settings = new VoiceProcessorSettings { NAudioPitchShiftEnabled = true, NAudioPitchShiftSemitones = 7,
                NAudioPitchShiftMix = 1, NAudioPitchShiftFftSize = 1024 };
            var processor = new NAudioPitchShiftProcessor(48000);
            processor.UpdateFromSettings(settings);
            processor.Transform(Enumerable.Range(0, 4000).Select(i => (float)(0.1 * Math.Sin(i * .1))).ToArray());
            if (bypass == "disabled") settings.NAudioPitchShiftEnabled = false;
            if (bypass == "dry") settings.NAudioPitchShiftMix = 0;
            if (bypass == "unity") settings.NAudioPitchShiftSemitones = 0;
            processor.UpdateFromSettings(settings);
            processor.Transform(new float[4800]);
            settings.NAudioPitchShiftEnabled = true;
            settings.NAudioPitchShiftMix = 1;
            settings.NAudioPitchShiftSemitones = 7;
            processor.UpdateFromSettings(settings);
            var silence = new float[4096];
            processor.Transform(silence);
            if (silence.Any(sample => Math.Abs(sample) > .00001)) throw new InvalidOperationException($"Pitch {bypass} replayed old audio.");
        }
    }

    public static void PlaybackStopAfterDisposeIsSafe()
    {
        var service = new MediaFoundationFilePlaybackService();
        service.Dispose();
        service.Stop();
        service.Dispose();
    }

    public static void MidiRejectsUnsupportedTimebases()
    {
        foreach (var division in new[] { 0, -1, 0xE728, 65536 })
        {
            try
            {
                MidiSequenceService.CreatePlaybackPlan(new MidiEventCollection(1, division), "unsupported.mid");
            }
            catch (NotSupportedException) { continue; }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException($"Unsupported MIDI division {division} was silently treated as musical ticks.");
        }
    }

    public static void MidiRejectsAsynchronousFileConversion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jericho-midi-format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "type2.mid");
            var destination = Path.Combine(root, "copy.mid");
            File.WriteAllBytes(path, [0x4d, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 2, 0, 1, 1, 0xe0,
                0x4d, 0x54, 0x72, 0x6b, 0, 0, 0, 4, 0, 0xff, 0x2f, 0]);
            foreach (Action operation in new Action[] { () => MidiSequenceService.CreatePlaybackPlan(path),
                         () => MidiFileService.ExportCopy(path, destination) })
            {
                try { operation(); }
                catch (NotSupportedException) { continue; }
                throw new InvalidOperationException("Independent MIDI sequences were silently converted to a shared timeline.");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static bool RunProcessProbe(string[] args)
    {
        if (args.Length == 0 || args[0] != "--karaoke-process-probe") return false;
        if (args[1] == "inherited-output")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--karaoke-process-probe");
            start.ArgumentList.Add("sleep");
            using var child = Process.Start(start)!;
        }
        else
        {
            if (args.Length > 2) File.WriteAllText(args[2], Environment.ProcessId.ToString());
            Console.WriteLine("probe started");
            Thread.Sleep(1500);
        }
        return true;
    }

    public static void KaraokeProcessDeadlineIncludesOutputDrain()
    {
        var run = typeof(JerichoDown.EqualizerWindow).GetMethod("RunKaraokeProcess", BindingFlags.NonPublic | BindingFlags.Static)!;
        var watch = Stopwatch.StartNew();
        try
        {
            run.Invoke(null, [Environment.ProcessPath!, new[] { "--karaoke-process-probe", "inherited-output" },
                AppContext.BaseDirectory, TimeSpan.FromMilliseconds(250), CancellationToken.None]);
            throw new InvalidOperationException("An inherited output pipe bypassed the tool's timeout.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is TimeoutException)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("Tool output drain ignored its deadline.");
        }
    }

    public static void KaraokeCancellationTerminatesChild()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"jericho-process-{Guid.NewGuid():N}.txt");
        using var cancellation = new CancellationTokenSource();
        var run = typeof(JerichoDown.EqualizerWindow).GetMethod("RunKaraokeProcess", BindingFlags.NonPublic | BindingFlags.Static)!;
        var worker = Task.Run(() =>
        {
            try
            {
                run.Invoke(null, [Environment.ProcessPath!, new[] { "--karaoke-process-probe", "sleep", marker },
                    AppContext.BaseDirectory, TimeSpan.FromSeconds(10), cancellation.Token]);
                throw new InvalidOperationException("Canceled tool completed normally.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { }
        });
        try
        {
            if (!SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0, 2000))
                throw new InvalidOperationException("Tool probe did not start.");
            using var child = Process.GetProcessById(int.Parse(File.ReadAllText(marker)));
            cancellation.Cancel();
            if (!worker.Wait(1000) || !child.WaitForExit(500))
                throw new InvalidOperationException("Canceled lyric tool was left running.");
        }
        finally
        {
            cancellation.Cancel();
            try { worker.Wait(3000); } finally { File.Delete(marker); }
        }
    }

    public static void MixerPreservesSummingHeadroom()
    {
        var output = new float[4];
        new MixBusProcessor().Process([2f, -2f, 0.5f, -0.5f], output, new MixBusSettings(25, false, false, -1));
        if (!output.SequenceEqual(new[] { 0.5f, -0.5f, 0.125f, -0.125f }))
            throw new InvalidOperationException("The sum was clipped before the master volume could attenuate it.");
    }

    public static void MonoFoldDownPrecedesClipping()
    {
        var output = new float[4];
        new MixBusProcessor().Process([2f, 0f, -2f, 0f], output,
            new MixBusSettings(100, false, false, -1, MixBusOutputMode.Mono));
        if (!output.SequenceEqual(new[] { 1f, 1f, -1f, -1f }))
            throw new InvalidOperationException("Mono fold-down clipped its channels before combining them.");
    }

    public static void LimiterProtectsPostEffectsOutput()
    {
        var settings = new VoiceProcessorSettings();
        foreach (var property in typeof(VoiceProcessorSettings).GetProperties().Where(p => p.PropertyType == typeof(bool) && p.CanWrite))
            property.SetValue(settings, false);
        settings.MakeupGainDb = 0;
        settings.LimiterEnabled = true;
        settings.LimiterCeilingDb = -18;
        settings.NAudioDmoParamEqEnabled = true;
        settings.NAudioDmoParamEqCenterFrequencyHz = 1000;
        settings.NAudioDmoParamEqGainDb = 15;
        var processor = new VoiceSampleProcessor(settings, 48000);
        var tone = Enumerable.Range(0, 48000).Select(i => (float)(0.8 * Math.Sin(2 * Math.PI * 1000 * i / 48000))).ToArray();
        var output = processor.Process(tone);
        var peak = output.Skip(12000).Max(Math.Abs);
        if (peak > Math.Pow(10, -18d / 20) + 0.001)
            throw new InvalidOperationException($"Post-limiter effect exceeded the -18 dB ceiling: {peak}.");
    }

    public static void ConvolutionDoesNotReplayBypassedAudio()
    {
        foreach (var disable in new[] { true, false })
        {
            var settings = new VoiceProcessorSettings { NAudioConvolutionEnabled = true, NAudioConvolutionMix = 1,
                NAudioConvolutionLengthMs = 160, NAudioConvolutionDecay = 0.9 };
            var processor = new NAudioImpulseConvolutionProcessor(48000);
            processor.UpdateFromSettings(settings);
            var pulse = new float[64];
            pulse[0] = 1;
            processor.Transform(pulse);
            if (disable) settings.NAudioConvolutionEnabled = false;
            else settings.NAudioConvolutionMix = 0;
            processor.UpdateFromSettings(settings);
            processor.Transform(new float[9600]);
            settings.NAudioConvolutionEnabled = true;
            settings.NAudioConvolutionMix = 1;
            processor.UpdateFromSettings(settings);
            var silence = new float[8000];
            processor.Transform(silence);
            if (silence.Any(sample => Math.Abs(sample) > 0.00001))
                throw new InvalidOperationException("Re-enabling convolution replayed audio from before bypass.");
        }
    }

    public static void SeekInterruptsOldFrameWait()
    {
        using var service = new MediaFoundationFilePlaybackService();
        using var cancellation = new CancellationTokenSource();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((Stopwatch)typeof(MediaFoundationFilePlaybackService).GetField("_playbackClock", flags)!.GetValue(service)!).Start();
        var wait = typeof(MediaFoundationFilePlaybackService).GetMethod("WaitUntilFrameIsDue", flags)!;
        var worker = Task.Run(() => wait.Invoke(service, [TimeSpan.FromSeconds(10).Ticks, cancellation.Token]));
        try
        {
            Thread.Sleep(40);
            service.Seek(TimeSpan.Zero);
            if (!worker.Wait(TimeSpan.FromMilliseconds(300)))
                throw new InvalidOperationException("Seeking backward left playback waiting for an old frame.");
        }
        finally
        {
            cancellation.Cancel();
            try { worker.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        }
    }
}
