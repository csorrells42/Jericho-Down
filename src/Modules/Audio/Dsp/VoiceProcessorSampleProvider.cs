using NAudio.Wave;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed class VoiceProcessorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly bool _bypassProcessing;
    private float[] _inputScratch = [];
    private float[] _lastProcessedSamples = [];

    public VoiceProcessorSampleProvider(ISampleProvider source, VoiceSampleProcessor processor, bool bypassProcessing = false)
    {
        _source = source;
        Processor = processor;
        _bypassProcessing = bypassProcessing;
    }

    public VoiceSampleProcessor Processor { get; }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int GraphicEqualizerLatencySamples => Processor.GraphicEqualizerLatencySamples;

    public double GraphicEqualizerLatencyMilliseconds => Processor.GraphicEqualizerLatencyMilliseconds;

    public int LimiterLookaheadLatencySamples => Processor.LimiterLookaheadLatencySamples;

    public double LimiterLookaheadLatencyMilliseconds => Processor.LimiterLookaheadLatencyMilliseconds;

    public int KnownDspAlgorithmicLatencySamples => Processor.KnownDspAlgorithmicLatencySamples;

    public double KnownDspAlgorithmicLatencyMilliseconds => Processor.KnownDspAlgorithmicLatencyMilliseconds;

    public int LastProcessedSampleCount { get; private set; }

    public ReadOnlySpan<float> LastProcessedSamples => _lastProcessedSamples.AsSpan(0, LastProcessedSampleCount);

    public int Read(float[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (_inputScratch.Length < count)
        {
            _inputScratch = new float[count];
        }

        var read = _source.Read(_inputScratch, 0, count);
        if (read <= 0)
        {
            LastProcessedSampleCount = 0;
            return 0;
        }

        if (_bypassProcessing)
        {
            _inputScratch.AsSpan(0, read).CopyTo(buffer.AsSpan(offset, read));
        }
        else
        {
            Processor.Process(_inputScratch.AsSpan(0, read), buffer.AsSpan(offset, read));
        }

        if (_lastProcessedSamples.Length < read)
        {
            _lastProcessedSamples = new float[read];
        }

        Array.Copy(buffer, offset, _lastProcessedSamples, 0, read);
        LastProcessedSampleCount = read;
        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
        }

        return count;
    }
}
