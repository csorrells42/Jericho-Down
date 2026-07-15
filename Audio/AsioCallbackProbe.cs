using System.Globalization;
using System.Text;
using NAudio.Wave;

namespace JerichoDown.Audio;

public static class AsioCallbackProbe
{
    private const int MaximumProbeInputChannels = 10;

    public static string BuildReport(string driverName, int sampleRate, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new ArgumentException("Choose a valid ASIO driver.", nameof(driverName));
        }

        sampleRate = Math.Max(8000, sampleRate);
        duration = duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(900) : duration;
        var trimmedDriverName = driverName.Trim();
        var results = new[]
        {
            RunMode(trimmedDriverName, sampleRate, duration, ProbeMode.OutputOnly),
            RunMode(trimmedDriverName, sampleRate, duration, ProbeMode.RecordOnly),
            RunMode(trimmedDriverName, sampleRate, duration, ProbeMode.FullDuplex)
        };

        var report = new StringBuilder();
        report.AppendLine("ASIO Callback Test");
        report.AppendLine($"Generated: {DateTime.Now:G}");
        report.AppendLine();
        report.AppendLine($"Driver: {trimmedDriverName}");
        report.AppendLine($"Sample rate: {sampleRate.ToString(CultureInfo.InvariantCulture)} Hz");
        report.AppendLine($"Each mode duration: {duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms");
        report.AppendLine();
        foreach (var result in results)
        {
            AppendMode(report, result);
            report.AppendLine();
        }

        AppendInterpretation(report, results);
        return report.ToString();
    }

    private static ProbeModeResult RunMode(string driverName, int sampleRate, TimeSpan duration, ProbeMode mode)
    {
        using var dispatcher = new StaThreadDispatcher($"Jericho ASIO Callback Test ({mode})");
        AsioOut? asio = null;
        CountingSilentWaveProvider? outputProvider = null;
        EventHandler<AsioAudioAvailableEventArgs>? audioAvailable = null;
        var driverInputChannels = 0;
        var driverOutputChannels = 0;
        var inputChannels = 0;
        var outputChannels = 0;
        int? framesPerBuffer = null;
        var started = false;
        var playbackStateAfterPlay = "not started";
        var playbackStateAfterWait = "not started";
        var audioCallbacks = 0L;
        long firstCallbackTicks = 0;
        long lastCallbackTicks = 0;
        string? error = null;
        string? stopError = null;

        try
        {
            dispatcher.Invoke(() =>
            {
                asio = new AsioOut(driverName);
                driverInputChannels = asio.DriverInputChannelCount;
                driverOutputChannels = asio.DriverOutputChannelCount;
                inputChannels = mode == ProbeMode.OutputOnly
                    ? 0
                    : Math.Clamp(driverInputChannels, 1, MaximumProbeInputChannels);
                outputChannels = mode == ProbeMode.RecordOnly
                    ? 0
                    : Math.Min(2, Math.Max(0, driverOutputChannels));

                if (mode != ProbeMode.OutputOnly && inputChannels <= 0)
                {
                    throw new InvalidOperationException("The ASIO driver reported no input channels for recording.");
                }

                if (mode != ProbeMode.RecordOnly && outputChannels <= 0)
                {
                    throw new InvalidOperationException("The ASIO driver reported no output channels for callback clock testing.");
                }

                if (mode != ProbeMode.OutputOnly)
                {
                    audioAvailable = (_, _) =>
                    {
                        var count = Interlocked.Increment(ref audioCallbacks);
                        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
                        if (count == 1)
                        {
                            Interlocked.Exchange(ref firstCallbackTicks, nowTicks);
                        }

                        Interlocked.Exchange(ref lastCallbackTicks, nowTicks);
                    };
                    asio.AudioAvailable += audioAvailable;
                }

                if (mode == ProbeMode.RecordOnly)
                {
                    asio.InitRecordAndPlayback(null, inputChannels, sampleRate);
                }
                else
                {
                    outputProvider = new CountingSilentWaveProvider(sampleRate, outputChannels);
                    if (mode == ProbeMode.OutputOnly)
                    {
                        asio.Init(outputProvider);
                    }
                    else
                    {
                        asio.InitRecordAndPlayback(outputProvider, inputChannels, sampleRate);
                    }
                }

                framesPerBuffer = asio.FramesPerBuffer;
                asio.Play();
                started = true;
                playbackStateAfterPlay = asio.PlaybackState.ToString();
            });

            Thread.Sleep(duration);
            dispatcher.Invoke(() =>
            {
                playbackStateAfterWait = asio?.PlaybackState.ToString() ?? "not open";
            });
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            try
            {
                dispatcher.Invoke(() =>
                {
                    if (asio is null)
                    {
                        return;
                    }

                    if (audioAvailable is not null)
                    {
                        asio.AudioAvailable -= audioAvailable;
                    }

                    try
                    {
                        asio.Stop();
                    }
                    catch (Exception ex)
                    {
                        stopError = ex.Message;
                    }

                    asio.Dispose();
                    asio = null;
                });
            }
            catch (Exception ex) when (stopError is null)
            {
                stopError = ex.Message;
            }
        }

        return new ProbeModeResult(
            GetModeName(mode),
            started,
            error,
            stopError,
            driverInputChannels,
            driverOutputChannels,
            inputChannels,
            outputChannels,
            framesPerBuffer,
            playbackStateAfterPlay,
            playbackStateAfterWait,
            Interlocked.Read(ref audioCallbacks),
            outputProvider?.ReadCount ?? 0,
            outputProvider?.BytesRead ?? 0,
            ReadUtc(firstCallbackTicks),
            ReadUtc(lastCallbackTicks));
    }

    private static void AppendMode(StringBuilder report, ProbeModeResult result)
    {
        report.AppendLine(result.ModeName);
        report.AppendLine($"- Started: {(result.Started ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            report.AppendLine($"- Start error: {result.Error}");
        }

        if (!string.IsNullOrWhiteSpace(result.StopError))
        {
            report.AppendLine($"- Stop error: {result.StopError}");
        }

        report.AppendLine($"- Driver input channels: {result.DriverInputChannels}");
        report.AppendLine($"- Driver output channels: {result.DriverOutputChannels}");
        report.AppendLine($"- Test input channels: {result.InputChannels}");
        report.AppendLine($"- Test output channels: {result.OutputChannels}");
        report.AppendLine($"- Frames per buffer: {FormatNullable(result.FramesPerBuffer)}");
        report.AppendLine($"- Playback state after Play(): {result.PlaybackStateAfterPlay}");
        report.AppendLine($"- Playback state after wait: {result.PlaybackStateAfterWait}");
        report.AppendLine($"- AudioAvailable callbacks: {result.AudioCallbacks}");
        report.AppendLine($"- Silent output reads: {result.OutputReads}");
        report.AppendLine($"- Silent output bytes: {result.OutputBytes}");
        report.AppendLine($"- First input callback UTC: {FormatUtc(result.FirstCallbackUtc)}");
        report.AppendLine($"- Last input callback UTC: {FormatUtc(result.LastCallbackUtc)}");
    }

    private static void AppendInterpretation(StringBuilder report, IReadOnlyList<ProbeModeResult> results)
    {
        var outputOnly = results.FirstOrDefault(result => result.ModeName == GetModeName(ProbeMode.OutputOnly));
        var recordOnly = results.FirstOrDefault(result => result.ModeName == GetModeName(ProbeMode.RecordOnly));
        var fullDuplex = results.FirstOrDefault(result => result.ModeName == GetModeName(ProbeMode.FullDuplex));
        var outputClockRan = (outputOnly?.OutputReads ?? 0) > 0 || (fullDuplex?.OutputReads ?? 0) > 0;
        var inputCallbacksRan = (recordOnly?.AudioCallbacks ?? 0) > 0 || (fullDuplex?.AudioCallbacks ?? 0) > 0;

        report.AppendLine("Interpretation");
        if (!outputClockRan && !inputCallbacksRan)
        {
            report.AppendLine("- The Focusrite ASIO driver opened, but no tested ASIO mode produced buffer activity inside Jericho.");
            report.AppendLine("- That points below DSP, mixer, graphs, and channel routing: the driver is accepting Play() but not running its callback loop for this process.");
            report.AppendLine("- Close DAWs, OBS/audio tools, browser audio apps using the interface, and Focusrite Control if it is changing clock/routing, then rerun this test.");
            return;
        }

        if (outputClockRan && !inputCallbacksRan)
        {
            report.AppendLine("- ASIO output callbacks are running, but ASIO input callbacks are not being delivered.");
            report.AppendLine("- That narrows the problem to the driver's input-buffer path, Focusrite routing to ASIO input lanes, or a driver-side input permission/clock refusal.");
            return;
        }

        if (!outputClockRan && inputCallbacksRan)
        {
            report.AppendLine("- ASIO input callbacks work without the silent output clock.");
            report.AppendLine("- Jericho should use record-only ASIO input mode for this driver instead of full-duplex clocking.");
            return;
        }

        report.AppendLine("- ASIO callbacks work in the standalone probe. If live input still fails, the remaining issue is interaction between the live service startup and the selected ASIO mode.");
    }

    private static string GetModeName(ProbeMode mode)
    {
        return mode switch
        {
            ProbeMode.OutputOnly => "Output-only silent callback test",
            ProbeMode.RecordOnly => "Record-only input callback test",
            ProbeMode.FullDuplex => "Full-duplex input plus silent output callback test",
            _ => mode.ToString()
        };
    }

    private static DateTimeOffset? ReadUtc(long ticks)
    {
        return ticks <= 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static string FormatNullable(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "not available";
    }

    private static string FormatUtc(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture) ?? "none";
    }

    private enum ProbeMode
    {
        OutputOnly,
        RecordOnly,
        FullDuplex
    }

    private sealed record ProbeModeResult(
        string ModeName,
        bool Started,
        string? Error,
        string? StopError,
        int DriverInputChannels,
        int DriverOutputChannels,
        int InputChannels,
        int OutputChannels,
        int? FramesPerBuffer,
        string PlaybackStateAfterPlay,
        string PlaybackStateAfterWait,
        long AudioCallbacks,
        long OutputReads,
        long OutputBytes,
        DateTimeOffset? FirstCallbackUtc,
        DateTimeOffset? LastCallbackUtc);

    private sealed class CountingSilentWaveProvider : IWaveProvider
    {
        private long _readCount;
        private long _bytesRead;

        public CountingSilentWaveProvider(int sampleRate, int channels)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Math.Max(8000, sampleRate), Math.Max(1, channels));
        }

        public WaveFormat WaveFormat { get; }

        public long ReadCount => Interlocked.Read(ref _readCount);

        public long BytesRead => Interlocked.Read(ref _bytesRead);

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            Interlocked.Increment(ref _readCount);
            Interlocked.Add(ref _bytesRead, count);
            return count;
        }
    }
}
