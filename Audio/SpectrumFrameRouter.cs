namespace JerichoDown.Audio;

public static class SpectrumFrameRouter
{
    public static SpectrumFrame CreateSelectedMicFrame(
        SpectrumFrame frame,
        int selectedChannelNumber,
        bool selectedChannelHasSource,
        double[]? selectedInputMagnitudes = null)
    {
        selectedInputMagnitudes ??= [];
        var selectedLine = frame.MicrophoneLines.FirstOrDefault(line => line.ChannelNumber == selectedChannelNumber);
        if (selectedLine is null)
        {
            if (!selectedChannelHasSource)
            {
                return CreateEmptyFrame(frame);
            }

            return new SpectrumFrame(
                [],
                selectedInputMagnitudes,
                [],
                [],
                0d,
                selectedInputMagnitudes.Length > 0 ? frame.RawPeakLevel : 0d,
                frame.Telemetry,
                frame.SampleRate,
                frame.Input1Magnitudes,
                frame.Input2Magnitudes,
                frame.Input1PeakLevel,
                frame.Input2PeakLevel,
                frame.Input1Samples,
                frame.Input2Samples,
                frame.MicrophoneLines,
                frame.RmsLevel,
                frame.RawRmsLevel);
        }

        var rawMagnitudes = selectedLine.RawMagnitudes.Length > 0
            ? selectedLine.RawMagnitudes
            : selectedInputMagnitudes.Length > 0
            ? selectedInputMagnitudes
            : frame.RawMagnitudes;
        return new SpectrumFrame(
            selectedLine.Magnitudes,
            rawMagnitudes,
            selectedLine.ProcessedSamples,
            selectedLine.RawSamples,
            selectedLine.PeakLevel,
            selectedLine.RawPeakLevel,
            frame.Telemetry,
            frame.SampleRate,
            frame.Input1Magnitudes,
            frame.Input2Magnitudes,
            frame.Input1PeakLevel,
            frame.Input2PeakLevel,
            frame.Input1Samples,
            frame.Input2Samples,
            frame.MicrophoneLines,
            selectedLine.RmsLevel,
            selectedLine.RawRmsLevel);
    }

    public static SpectrumFrame CreateProgramOutputFrame(SpectrumFrame frame, double[]? recordingMagnitudes = null)
    {
        return new SpectrumFrame(
            frame.Magnitudes,
            recordingMagnitudes ?? [],
            frame.ProcessedSamples,
            [],
            frame.PeakLevel,
            recordingMagnitudes is { Length: > 0 } ? frame.RawPeakLevel : 0d,
            frame.Telemetry,
            frame.SampleRate,
            rmsLevel: frame.RmsLevel);
    }

    private static SpectrumFrame CreateEmptyFrame(SpectrumFrame frame)
    {
        return new SpectrumFrame(
            [],
            [],
            [],
            [],
            0d,
            0d,
            frame.Telemetry,
            frame.SampleRate);
    }
}
