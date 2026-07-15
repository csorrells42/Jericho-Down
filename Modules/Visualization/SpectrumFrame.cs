using JerichoDown.Modules.Audio.Dsp;

namespace JerichoDown.Modules.Visualization;

public sealed class MicrophoneSpectrumLine
{
    public MicrophoneSpectrumLine(
        int channelNumber,
        double[] magnitudes,
        double peakLevel,
        double[]? rawMagnitudes = null,
        double rawPeakLevel = 0d,
        float[]? processedSamples = null,
        float[]? rawSamples = null,
        double rmsLevel = 0d,
        double rawRmsLevel = 0d,
        double syncBufferedMilliseconds = 0d,
        double syncTargetLatencyMilliseconds = 0d,
        int syncUnderflowCount = 0,
        int syncDriftTrimCount = 0,
        double meteredPeakLevel = 0d)
    {
        ChannelNumber = channelNumber;
        Magnitudes = magnitudes;
        PeakLevel = peakLevel;
        RawMagnitudes = rawMagnitudes ?? [];
        RawPeakLevel = rawPeakLevel;
        ProcessedSamples = processedSamples ?? [];
        RawSamples = rawSamples ?? [];
        RmsLevel = rmsLevel;
        RawRmsLevel = rawRmsLevel;
        SyncBufferedMilliseconds = syncBufferedMilliseconds;
        SyncTargetLatencyMilliseconds = syncTargetLatencyMilliseconds;
        SyncUnderflowCount = syncUnderflowCount;
        SyncDriftTrimCount = syncDriftTrimCount;
        MeteredPeakLevel = meteredPeakLevel;
    }

    public int ChannelNumber { get; }

    public double[] Magnitudes { get; }

    public double PeakLevel { get; }

    public double[] RawMagnitudes { get; }

    public double RawPeakLevel { get; }

    public double RmsLevel { get; }

    public double RawRmsLevel { get; }

    public double SyncBufferedMilliseconds { get; }

    public double SyncTargetLatencyMilliseconds { get; }

    public int SyncUnderflowCount { get; }

    public int SyncDriftTrimCount { get; }

    public double MeteredPeakLevel { get; }

    public float[] ProcessedSamples { get; }

    public float[] RawSamples { get; }
}

public sealed class SpectrumFrame
{
    public SpectrumFrame(double[] magnitudes, double peakLevel)
        : this(magnitudes, magnitudes, [], [], peakLevel, peakLevel, new VoiceProcessingTelemetry())
    {
    }

    public SpectrumFrame(
        double[] magnitudes,
        double[] rawMagnitudes,
        float[] processedSamples,
        float[] rawSamples,
        double peakLevel,
        double rawPeakLevel,
        VoiceProcessingTelemetry telemetry,
        int sampleRate = 44100,
        double[]? input1Magnitudes = null,
        double[]? input2Magnitudes = null,
        double input1PeakLevel = 0d,
        double input2PeakLevel = 0d,
        float[]? input1Samples = null,
        float[]? input2Samples = null,
        IReadOnlyList<MicrophoneSpectrumLine>? microphoneLines = null,
        double rmsLevel = 0d,
        double rawRmsLevel = 0d)
    {
        Magnitudes = magnitudes;
        RawMagnitudes = rawMagnitudes;
        ProcessedSamples = processedSamples;
        RawSamples = rawSamples;
        PeakLevel = peakLevel;
        RawPeakLevel = rawPeakLevel;
        RmsLevel = rmsLevel;
        RawRmsLevel = rawRmsLevel;
        Telemetry = telemetry;
        SampleRate = sampleRate;
        Input1Magnitudes = input1Magnitudes ?? [];
        Input2Magnitudes = input2Magnitudes ?? [];
        Input1PeakLevel = input1PeakLevel;
        Input2PeakLevel = input2PeakLevel;
        Input1Samples = input1Samples ?? [];
        Input2Samples = input2Samples ?? [];
        MicrophoneLines = microphoneLines ?? [];
    }

    public double[] Magnitudes { get; }

    public double[] RawMagnitudes { get; }

    public float[] ProcessedSamples { get; }

    public float[] RawSamples { get; }

    public double PeakLevel { get; }

    public double RawPeakLevel { get; }

    public double RmsLevel { get; }

    public double RawRmsLevel { get; }

    public VoiceProcessingTelemetry Telemetry { get; }

    public int SampleRate { get; }

    public double[] Input1Magnitudes { get; }

    public double[] Input2Magnitudes { get; }

    public double Input1PeakLevel { get; }

    public double Input2PeakLevel { get; }

    public float[] Input1Samples { get; }

    public float[] Input2Samples { get; }

    public IReadOnlyList<MicrophoneSpectrumLine> MicrophoneLines { get; }

    public bool HasStereoInput => Input1Magnitudes.Length > 0 && Input2Magnitudes.Length > 0;
}


