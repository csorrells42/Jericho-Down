namespace JerichoDown.Audio;

public sealed class AudioDelayLine
{
    private readonly int _sampleRate;
    private readonly int _maximumDelaySamples;
    private readonly float[] _buffer;
    private int _writeIndex;

    public AudioDelayLine(int sampleRate, double maximumDelayMilliseconds)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _maximumDelaySamples = Math.Max(1, (int)Math.Ceiling(_sampleRate * Math.Max(1d, maximumDelayMilliseconds) / 1000d));
        _buffer = new float[_maximumDelaySamples + 1];
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _writeIndex = 0;
    }

    public void Process(Span<float> samples, double delayMilliseconds)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        var delaySamples = Math.Clamp(
            (int)Math.Round(Math.Max(0d, delayMilliseconds) * _sampleRate / 1000d),
            0,
            _maximumDelaySamples);

        for (var i = 0; i < samples.Length; i++)
        {
            var incoming = samples[i];
            var delayed = incoming;
            if (delaySamples > 0)
            {
                var readIndex = _writeIndex - delaySamples;
                if (readIndex < 0)
                {
                    readIndex += _buffer.Length;
                }

                delayed = _buffer[readIndex];
            }

            _buffer[_writeIndex] = incoming;
            _writeIndex++;
            if (_writeIndex >= _buffer.Length)
            {
                _writeIndex = 0;
            }

            samples[i] = delayed;
        }
    }
}
