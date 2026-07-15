using NAudio.Wave;
using JerichoDown.Modules.Audio.Dsp;

namespace JerichoDown.Audio;

public sealed class StereoVoiceProcessorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _inputScratch = [];
    private float[] _leftInputScratch = [];
    private float[] _rightInputScratch = [];
    private float[] _leftOutputScratch = [];
    private float[] _rightOutputScratch = [];
    private float[] _lastProcessedSamples = [];

    public StereoVoiceProcessorSampleProvider(ISampleProvider source, VoiceSampleProcessor leftProcessor, VoiceSampleProcessor rightProcessor)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException("Stereo voice processing requires a stereo source.", nameof(source));
        }

        _source = source;
        LeftProcessor = leftProcessor;
        RightProcessor = rightProcessor;
    }

    public VoiceSampleProcessor LeftProcessor { get; }

    public VoiceSampleProcessor RightProcessor { get; }

    public WaveFormat WaveFormat => _source.WaveFormat;

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

        var stereoRead = read - read % 2;
        var frameCount = stereoRead / 2;
        EnsureChannelScratch(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sourceOffset = frame * 2;
            _leftInputScratch[frame] = _inputScratch[sourceOffset];
            _rightInputScratch[frame] = _inputScratch[sourceOffset + 1];
        }

        LeftProcessor.Process(_leftInputScratch.AsSpan(0, frameCount), _leftOutputScratch.AsSpan(0, frameCount));
        RightProcessor.Process(_rightInputScratch.AsSpan(0, frameCount), _rightOutputScratch.AsSpan(0, frameCount));

        for (var frame = 0; frame < frameCount; frame++)
        {
            var targetOffset = offset + frame * 2;
            buffer[targetOffset] = _leftOutputScratch[frame];
            buffer[targetOffset + 1] = _rightOutputScratch[frame];
        }

        if (stereoRead < read)
        {
            buffer[offset + stereoRead] = 0f;
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

    private void EnsureChannelScratch(int frameCount)
    {
        if (_leftInputScratch.Length < frameCount)
        {
            _leftInputScratch = new float[frameCount];
            _rightInputScratch = new float[frameCount];
            _leftOutputScratch = new float[frameCount];
            _rightOutputScratch = new float[frameCount];
        }
    }
}
