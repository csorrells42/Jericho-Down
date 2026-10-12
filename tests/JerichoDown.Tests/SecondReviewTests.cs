using System.Diagnostics;
using System.Reflection;
using JerichoDown.Modules.Audio.Dsp;
using JerichoDown.Modules.Mixer;
using JerichoDown.Modules.SessionPlayback;

internal static class SecondReviewTests
{
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
