namespace JerichoDown.Modules.Audio.Sync;

public sealed class AudioStereoDelayLine
{
    private readonly int _sampleRate;
    private readonly int _maximumDelayFrames;
    private readonly float[] _leftBuffer;
    private readonly float[] _rightBuffer;
    private int _writeIndex;

    public AudioStereoDelayLine(int sampleRate, double maximumDelayMilliseconds)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _maximumDelayFrames = Math.Max(1, (int)Math.Ceiling(_sampleRate * Math.Max(1d, maximumDelayMilliseconds) / 1000d));
        _leftBuffer = new float[_maximumDelayFrames + 1];
        _rightBuffer = new float[_maximumDelayFrames + 1];
    }

    public void Reset()
    {
        Array.Clear(_leftBuffer);
        Array.Clear(_rightBuffer);
        _writeIndex = 0;
    }

    public void Process(Span<float> interleavedStereoSamples, double delayMilliseconds)
    {
        if (interleavedStereoSamples.IsEmpty)
        {
            return;
        }

        var delayFrames = Math.Clamp(
            (int)Math.Round(Math.Max(0d, delayMilliseconds) * _sampleRate / 1000d),
            0,
            _maximumDelayFrames);

        var sampleCount = interleavedStereoSamples.Length - interleavedStereoSamples.Length % 2;
        for (var i = 0; i < sampleCount; i += 2)
        {
            var incomingLeft = interleavedStereoSamples[i];
            var incomingRight = interleavedStereoSamples[i + 1];
            var delayedLeft = incomingLeft;
            var delayedRight = incomingRight;
            if (delayFrames > 0)
            {
                var readIndex = _writeIndex - delayFrames;
                if (readIndex < 0)
                {
                    readIndex += _leftBuffer.Length;
                }

                delayedLeft = _leftBuffer[readIndex];
                delayedRight = _rightBuffer[readIndex];
            }

            _leftBuffer[_writeIndex] = incomingLeft;
            _rightBuffer[_writeIndex] = incomingRight;
            _writeIndex++;
            if (_writeIndex >= _leftBuffer.Length)
            {
                _writeIndex = 0;
            }

            interleavedStereoSamples[i] = delayedLeft;
            interleavedStereoSamples[i + 1] = delayedRight;
        }
    }
}
