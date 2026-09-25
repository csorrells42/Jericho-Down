using System.Reflection;
using JerichoDown.Audio;
using NAudio.Wave;

internal static class CaptureLifecycleTests
{
    public static void DisposedServiceCannotReopen()
    {
        var service = new MicrophoneSpectrumService();
        service.Dispose();
        try
        {
            service.Start(AudioInputDevice.CreateStereoTestTone());
            throw new InvalidOperationException("A disposed service reopened audio capture.");
        }
        catch (ObjectDisposedException) { }
        finally { service.Stop(); }
    }

    public static void RestartWaitsForOldDriver()
    {
        using var service = new MicrophoneSpectrumService();
        using var oldCapture = new BlockingCapture();
        SetField(service, "_capture", oldCapture);
        try
        {
            service.RestartCapture(AudioInputDevice.CreateStereoTestTone(), null,
                InputChannelMode.StereoPair, TimeSpan.FromMilliseconds(100));
            throw new InvalidOperationException("A replacement stream opened while the old driver was stopping.");
        }
        catch (TimeoutException) { }
        finally
        {
            oldCapture.AllowStop.Set();
            if (!oldCapture.Disposed.Wait(TimeSpan.FromSeconds(3)))
                throw new InvalidOperationException("The old capture was not released after its stop completed.");
        }
    }

    public static void StopCancelsPendingRecovery()
    {
        using var service = new MicrophoneSpectrumService();
        service.Start(AudioInputDevice.CreateStereoTestTone());
        var capture = (IWaveIn)typeof(MicrophoneSpectrumService)
            .GetField("_capture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        capture.StopRecording();
        service.Stop();
        Thread.Sleep(700);
        if (service.IsRunning)
            throw new InvalidOperationException("Queued recovery reopened capture after an explicit stop.");
    }

    public static void LiveToneRecordsAndRestarts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jericho-lifecycle-{Guid.NewGuid():N}.wav");
        try
        {
            using var service = new MicrophoneSpectrumService();
            var device = AudioInputDevice.CreateStereoTestTone();
            service.Start(device, null, InputChannelMode.StereoPair);
            service.StartProcessedAudioRecording(path);
            Thread.Sleep(250);
            service.PauseProcessedAudioRecording();
            if (!service.IsProcessedAudioRecordingPaused)
                throw new InvalidOperationException("Recording did not pause.");
            service.ResumeProcessedAudioRecording();
            Thread.Sleep(150);
            service.StopProcessedAudioRecording();
            service.RestartCapture(device, null, InputChannelMode.StereoPair, TimeSpan.FromSeconds(2));
            Thread.Sleep(100);
            if (!service.IsRunning || service.AreAudioCallbacksStale(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException("Live capture did not recover after restart.");
            service.Stop();
            using var reader = new AudioFileReader(path);
            var samples = new float[reader.WaveFormat.SampleRate];
            var count = reader.Read(samples, 0, samples.Length);
            if (count < 4800 || !samples.Take(count).Any(sample => Math.Abs(sample) > 0.01f))
                throw new InvalidOperationException("Live recording did not contain the generated audio.");
        }
        finally { File.Delete(path); }
    }

    private static void SetField(MicrophoneSpectrumService service, string name, object value) =>
        typeof(MicrophoneSpectrumService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, value);

    private sealed class BlockingCapture : IWaveIn
    {
        public readonly ManualResetEventSlim AllowStop = new(false);
        public readonly ManualResetEventSlim Disposed = new(false);
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public event EventHandler<WaveInEventArgs>? DataAvailable { add { } remove { } }
        public event EventHandler<StoppedEventArgs>? RecordingStopped { add { } remove { } }
        public void StartRecording() { }
        public void StopRecording() => AllowStop.Wait(TimeSpan.FromSeconds(5));
        public void Dispose() => Disposed.Set();
    }
}
