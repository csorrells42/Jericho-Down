namespace JerichoDown.Audio;

public static class MicCompareFrameRouter
{
    public static MicCompareFrame CreateFrame(
        SpectrumFrame frame,
        int mic1ChannelNumber = 1,
        int mic2ChannelNumber = 2)
    {
        var mic1Line = frame.MicrophoneLines.FirstOrDefault(line => line.ChannelNumber == mic1ChannelNumber);
        var mic2Line = frame.MicrophoneLines.FirstOrDefault(line => line.ChannelNumber == mic2ChannelNumber);
        return new MicCompareFrame(
            SelectMagnitudes(frame, mic1Line, mic1ChannelNumber),
            SelectMagnitudes(frame, mic2Line, mic2ChannelNumber),
            SelectSamples(frame, mic1Line, mic1ChannelNumber),
            SelectSamples(frame, mic2Line, mic2ChannelNumber));
    }

    private static double[] SelectMagnitudes(SpectrumFrame frame, MicrophoneSpectrumLine? line, int channelNumber)
    {
        if (line?.RawMagnitudes.Length > 0)
        {
            return line.RawMagnitudes;
        }

        if (line?.Magnitudes.Length > 0)
        {
            return line.Magnitudes;
        }

        return channelNumber switch
        {
            1 => frame.Input1Magnitudes,
            2 => frame.Input2Magnitudes,
            _ => []
        };
    }

    private static float[] SelectSamples(SpectrumFrame frame, MicrophoneSpectrumLine? line, int channelNumber)
    {
        if (line?.RawSamples.Length > 0)
        {
            return line.RawSamples;
        }

        if (line?.ProcessedSamples.Length > 0)
        {
            return line.ProcessedSamples;
        }

        return channelNumber switch
        {
            1 => frame.Input1Samples,
            2 => frame.Input2Samples,
            _ => []
        };
    }
}

public sealed record MicCompareFrame(
    double[] Mic1Magnitudes,
    double[] Mic2Magnitudes,
    float[] Mic1Samples,
    float[] Mic2Samples)
{
    public bool HasBothMagnitudes => Mic1Magnitudes.Length > 0 && Mic2Magnitudes.Length > 0;

    public bool HasBothSamples => Mic1Samples.Length > 0 && Mic2Samples.Length > 0;

    public bool HasAnalysisSources => HasBothMagnitudes && HasBothSamples;
}
