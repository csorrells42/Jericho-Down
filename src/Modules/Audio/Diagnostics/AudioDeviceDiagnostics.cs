using System.Text;
using JerichoDown.Modules.Audio.Asio;
using JerichoDown.Modules.Audio.Devices;
using JerichoDown.Modules.Audio.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;

namespace JerichoDown.Modules.Audio.Diagnostics;

public static class AudioDeviceDiagnostics
{
    public static string BuildReport(
        AudioInputDevice? selectedInput,
        AudioOutputDevice? selectedOutput,
        AudioDeviceFormat? inputFormat,
        AudioDeviceFormat? outputFormat,
        IReadOnlyList<AudioInputDevice> inputDevices,
        IReadOnlyList<AudioOutputDevice> outputDevices,
        AsioInputCaptureDiagnostics? asioInputDiagnostics = null,
        string? activeInputStatus = null,
        VoiceProcessingTelemetry? liveTelemetry = null,
        WasapiOutputSettings? wasapiOutputSettings = null)
    {
        var report = new StringBuilder();
        report.AppendLine("Audio Device Diagnostics");
        report.AppendLine($"Generated: {DateTime.Now:G}");
        report.AppendLine();
        report.AppendLine($"Input devices found: {inputDevices.Count}");
        report.AppendLine($"Output devices found: {outputDevices.Count}");
        report.AppendLine();

        AppendInputSection(report, selectedInput, inputFormat);
        AppendAsioRuntimeSection(report, selectedInput, asioInputDiagnostics, activeInputStatus);
        AppendLiveDspLatencySection(report, liveTelemetry);
        report.AppendLine();
        AppendOutputSection(report, selectedOutput, outputFormat);
        AppendProcessedMonitorOutputSection(report, selectedOutput, wasapiOutputSettings);
        report.AppendLine();
        AppendWarnings(report, selectedInput, selectedOutput, inputFormat, outputFormat);

        return report.ToString().TrimEnd();
    }

    private static void AppendLiveDspLatencySection(StringBuilder report, VoiceProcessingTelemetry? telemetry)
    {
        if (telemetry is null)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("Live DSP / Monitor Latency");
        report.AppendLine($"- Graphic EQ algorithmic latency: {telemetry.GraphicEqualizerLatencySamples} sample(s) ({telemetry.GraphicEqualizerLatencyMilliseconds:0.00} ms)");
        report.AppendLine($"- Limiter lookahead latency: {telemetry.LimiterLookaheadLatencySamples} sample(s) ({telemetry.LimiterLookaheadLatencyMilliseconds:0.00} ms)");
        report.AppendLine($"- Known DSP algorithmic latency: {telemetry.KnownDspAlgorithmicLatencySamples} sample(s) ({telemetry.KnownDspAlgorithmicLatencyMilliseconds:0.00} ms)");
        report.AppendLine("- Driver, capture, sync, and output buffering are separate from this DSP latency.");
    }

    private static void AppendProcessedMonitorOutputSection(
        StringBuilder report,
        AudioOutputDevice? selectedOutput,
        WasapiOutputSettings? wasapiOutputSettings)
    {
        if (wasapiOutputSettings is null || selectedOutput?.IsAsio == true)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("Processed Monitor Output");
        report.AppendLine($"- WASAPI profile: {wasapiOutputSettings.DisplayText}");
        report.AppendLine($"- Driver/output latency request: {wasapiOutputSettings.EffectiveLatencyMilliseconds} ms");
        report.AppendLine($"- Initial monitor buffer: {wasapiOutputSettings.ProcessedOutputInitialBufferDuration.TotalMilliseconds:0} ms");
        report.AppendLine($"- Target monitor buffer: {wasapiOutputSettings.ProcessedOutputTargetBufferDuration.TotalMilliseconds:0} ms");
        report.AppendLine($"- Maximum monitor buffer before trimming: {wasapiOutputSettings.ProcessedOutputMaximumBufferDuration.TotalMilliseconds:0} ms");
    }

    private static void AppendInputSection(StringBuilder report, AudioInputDevice? selectedInput, AudioDeviceFormat? inputFormat)
    {
        report.AppendLine("Selected Input");
        if (selectedInput is null)
        {
            report.AppendLine("- No selected input.");
            return;
        }

        report.AppendLine($"- Name: {selectedInput.Name}");
        report.AppendLine($"- Backend: {selectedInput.Backend}");
        report.AppendLine($"- Channels: {selectedInput.MaximumInputChannels}");
        report.AppendLine($"- Format: {Format(inputFormat)}");
        report.AppendLine($"- Endpoint: {FormatEndpointId(selectedInput.EndpointId)}");

        if (selectedInput.IsAsio)
        {
            report.AppendLine("- Driver: ASIO input");
            return;
        }

        if (selectedInput.IsStereoTestTone)
        {
            report.AppendLine("- Source: NAudio SignalGenerator stereo test tone");
            return;
        }

        if (selectedInput.IsSystemAudioLoopback)
        {
            AppendDefaultEndpointSummary(report, DataFlow.Render, "Windows loopback source");
            return;
        }

        if (selectedInput.IsProcessLoopback
            && AudioInputDevice.TryGetProcessLoopbackTargetProcessId(selectedInput.DeviceNumber, selectedInput.EndpointId, out var processId))
        {
            report.AppendLine($"- Process loopback target PID: {processId}");
            return;
        }

        AppendMatchingEndpointSummary(report, DataFlow.Capture, selectedInput.EndpointId, selectedInput.Name, "Windows capture endpoint");
    }

    private static void AppendOutputSection(StringBuilder report, AudioOutputDevice? selectedOutput, AudioDeviceFormat? outputFormat)
    {
        report.AppendLine("Selected Output");
        if (selectedOutput is null)
        {
            report.AppendLine("- No selected output.");
            return;
        }

        report.AppendLine($"- Name: {selectedOutput.Name}");
        report.AppendLine($"- Backend: {selectedOutput.Backend}");
        report.AppendLine($"- Format: {Format(outputFormat)}");
        report.AppendLine($"- Endpoint: {FormatEndpointId(selectedOutput.EndpointId)}");

        if (selectedOutput.IsAsio)
        {
            report.AppendLine("- Driver: ASIO output");
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedOutput.EndpointId))
        {
            AppendDefaultEndpointSummary(report, DataFlow.Render, "Windows playback endpoint");
        }
        else
        {
            AppendMatchingEndpointSummary(report, DataFlow.Render, selectedOutput.EndpointId, selectedOutput.Name, "Windows playback endpoint");
        }
    }

    private static void AppendAsioRuntimeSection(
        StringBuilder report,
        AudioInputDevice? selectedInput,
        AsioInputCaptureDiagnostics? diagnostics,
        string? activeInputStatus)
    {
        if (selectedInput?.IsAsio != true || diagnostics is null)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("ASIO Runtime Diagnostics");
        report.AppendLine($"- Active stream: {FormatStatus(activeInputStatus)}");
        report.AppendLine($"- Driver: {diagnostics.DriverName}");
        report.AppendLine($"- Requested sample rate: {diagnostics.RequestedSampleRate} Hz");
        report.AppendLine($"- Requested input channels: {diagnostics.RequestedChannelCount}");
        report.AppendLine($"- Driver input channels: {diagnostics.DriverInputChannelCount}");
        report.AppendLine($"- Driver output channels: {diagnostics.DriverOutputChannelCount}");
        report.AppendLine($"- Effective input channels opened: {diagnostics.EffectiveInputChannelCount}");
        report.AppendLine($"- Input channel offset: {diagnostics.InputChannelOffset}");
        report.AppendLine($"- Silent output clock: {(diagnostics.UsesSilentOutputClock ? $"{diagnostics.OutputClockChannelCount} channel(s)" : "disabled (record-only input)")}");
        report.AppendLine($"- ASIO thread: {diagnostics.ThreadApartmentState}, sync context {(diagnostics.HasSynchronizationContext ? "present" : "missing")}, managed id {diagnostics.ManagedThreadId}");
        report.AppendLine($"- Init started UTC: {diagnostics.InitStartedUtc:O}");
        report.AppendLine($"- Play started UTC: {(diagnostics.PlayStartedUtc.HasValue ? diagnostics.PlayStartedUtc.Value.ToString("O") : "not reached")}");
        report.AppendLine($"- Audio callbacks received: {diagnostics.AudioCallbackCount}");
        report.AppendLine($"- Last callback UTC: {(diagnostics.LastAudioCallbackUtc.HasValue ? diagnostics.LastAudioCallbackUtc.Value.ToString("O") : "none")}");

        if (diagnostics.AudioCallbackCount == 0 && diagnostics.PlayStartedUtc.HasValue)
        {
            report.AppendLine();
            report.AppendLine("No-callback diagnosis");
            report.AppendLine("- Jericho Down successfully identified the Focusrite ASIO driver, opened input buffers, and called Play().");
            report.AppendLine("- The failure is below the app's DSP, mixer, and graph code: NAudio did not receive any ASIO buffer-switch/audio callbacks from the driver.");
            report.AppendLine("- Most likely causes are: Focusrite driver clock/sample-rate mismatch, another ASIO client holding the interface, Focusrite Control not routing hardware inputs to the ASIO lanes, or the driver refusing callbacks for the opened channel/rate combination.");
            report.AppendLine("- Jericho Down has stopped automatic ASIO retries for this device so the Focusrite driver is not reopened repeatedly.");
            report.AppendLine("- Use Refresh Audio Devices or reselect the ASIO input after changing Focusrite Control, clock source, sample rate, or closing another ASIO app.");
        }
    }

    private static void AppendWarnings(
        StringBuilder report,
        AudioInputDevice? selectedInput,
        AudioOutputDevice? selectedOutput,
        AudioDeviceFormat? inputFormat,
        AudioDeviceFormat? outputFormat)
    {
        var warnings = new List<string>();
        if (selectedInput is null)
        {
            warnings.Add("No mixer input is selected.");
        }

        if (selectedOutput is null)
        {
            warnings.Add("No output device is selected.");
        }

        if (selectedInput?.IsSystemAudioLoopback == true)
        {
            warnings.Add("Computer audio loopback can feed back if monitored through the same speakers.");
        }

        if (selectedInput?.IsProcessLoopback == true)
        {
            warnings.Add("App audio inputs depend on the selected app session; refresh audio devices if the app closes or reopens.");
        }

        if (selectedOutput?.IsAsio == true)
        {
            warnings.Add("ASIO output bypasses Windows CoreAudio app-session controls.");
        }

        if (inputFormat is not null
            && outputFormat is not null
            && inputFormat.Value.SampleRate != outputFormat.Value.SampleRate)
        {
            warnings.Add($"Input/output sample rates differ ({inputFormat.Value.SampleRate} -> {outputFormat.Value.SampleRate}); output resampling will be used.");
        }

        report.AppendLine("Warnings");
        if (warnings.Count == 0)
        {
            report.AppendLine("- No obvious audio device warnings.");
            return;
        }

        foreach (var warning in warnings)
        {
            report.AppendLine($"- {warning}");
        }
    }

    private static void AppendDefaultEndpointSummary(StringBuilder report, DataFlow flow, string label)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            AppendEndpointSummary(report, endpoint, label);
        }
        catch (Exception ex)
        {
            report.AppendLine($"- {label}: unavailable ({ex.Message})");
        }
    }

    private static void AppendMatchingEndpointSummary(
        StringBuilder report,
        DataFlow flow,
        string? endpointId,
        string deviceName,
        string label)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = !string.IsNullOrWhiteSpace(endpointId)
                ? enumerator.GetDevice(endpointId)
                : enumerator
                    .EnumerateAudioEndPoints(flow, DeviceState.All)
                    .FirstOrDefault(device => DeviceNamesMatch(deviceName, device.FriendlyName));
            if (endpoint is null)
            {
                report.AppendLine($"- {label}: no matching Windows endpoint found.");
                return;
            }

            AppendEndpointSummary(report, endpoint, label);
        }
        catch (Exception ex)
        {
            report.AppendLine($"- {label}: unavailable ({ex.Message})");
        }
    }

    private static void AppendEndpointSummary(StringBuilder report, MMDevice endpoint, string label)
    {
        report.AppendLine($"- {label}: {endpoint.FriendlyName}");
        report.AppendLine($"- State: {endpoint.State}");
        TryAppend(report, "Windows mix format", () => DescribeWaveFormat(endpoint.AudioClient.MixFormat));
        TryAppend(report, "Endpoint volume", () => $"{endpoint.AudioEndpointVolume.MasterVolumeLevelScalar:P0}");
        TryAppend(report, "Endpoint mute", () => endpoint.AudioEndpointVolume.Mute ? "muted" : "unmuted");
        TryAppend(report, "Current peak", () => $"{endpoint.AudioMeterInformation.MasterPeakValue:P0}");
    }

    private static void TryAppend(StringBuilder report, string label, Func<string> valueFactory)
    {
        try
        {
            report.AppendLine($"- {label}: {valueFactory()}");
        }
        catch (Exception ex)
        {
            report.AppendLine($"- {label}: unavailable ({ex.Message})");
        }
    }

    private static string Format(AudioDeviceFormat? format)
    {
        return format?.ToString() ?? "not available";
    }

    private static string FormatStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "not available" : status;
    }

    private static string FormatEndpointId(string? endpointId)
    {
        return string.IsNullOrWhiteSpace(endpointId) ? "default or not mapped" : endpointId;
    }

    private static string DescribeWaveFormat(WaveFormat format)
    {
        var encoding = format.Encoding == WaveFormatEncoding.IeeeFloat
            || format is WaveFormatExtensible extensible
            && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT
                ? "float"
                : "PCM";
        var bits = encoding == "float" ? 32 : format.BitsPerSample;
        return $"{format.SampleRate / 1000d:0.#} kHz, {format.Channels} ch, {bits}-bit {encoding}";
    }

    private static bool DeviceNamesMatch(string first, string second)
    {
        var normalizedFirst = NormalizeDeviceName(first);
        var normalizedSecond = NormalizeDeviceName(second);
        return !string.IsNullOrWhiteSpace(normalizedFirst)
            && !string.IsNullOrWhiteSpace(normalizedSecond)
            && (normalizedFirst.Contains(normalizedSecond, StringComparison.OrdinalIgnoreCase)
                || normalizedSecond.Contains(normalizedFirst, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDeviceName(string value)
    {
        var trimmed = value.Trim();
        var parenthesis = trimmed.IndexOf('(');
        if (parenthesis > 0)
        {
            trimmed = trimmed[..parenthesis].Trim();
        }

        return trimmed;
    }
}
