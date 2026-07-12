using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;

namespace JerichoDown.Audio;

public static class AudioDeviceDiagnostics
{
    public static string BuildReport(
        AudioInputDevice? selectedInput,
        AudioOutputDevice? selectedOutput,
        AudioDeviceFormat? inputFormat,
        AudioDeviceFormat? outputFormat,
        IReadOnlyList<AudioInputDevice> inputDevices,
        IReadOnlyList<AudioOutputDevice> outputDevices)
    {
        var report = new StringBuilder();
        report.AppendLine("Audio Device Diagnostics");
        report.AppendLine($"Generated: {DateTime.Now:G}");
        report.AppendLine();
        report.AppendLine($"Input devices found: {inputDevices.Count}");
        report.AppendLine($"Output devices found: {outputDevices.Count}");
        report.AppendLine();

        AppendInputSection(report, selectedInput, inputFormat);
        report.AppendLine();
        AppendOutputSection(report, selectedOutput, outputFormat);
        report.AppendLine();
        AppendWarnings(report, selectedInput, selectedOutput, inputFormat, outputFormat);

        return report.ToString().TrimEnd();
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
