using NAudio.Wave;

namespace JerichoDown.Modules.Mixer;

public sealed class LiveStereoBlockSampleProvider : ISampleProvider
{
    private float[] _samples = [];
    private int _length;
    private int _position;

    public LiveStereoBlockSampleProvider(int sampleRate)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Math.Max(8000, sampleRate), 2);
    }

    public WaveFormat WaveFormat { get; }

    public int LastSampleCount => _length;

    public ReadOnlySpan<float> LastSamples => _samples.AsSpan(0, _length);

    public void SetBlock(ReadOnlySpan<float> interleavedStereoSamples)
    {
        var length = interleavedStereoSamples.Length - interleavedStereoSamples.Length % 2;
        if (_samples.Length < length)
        {
            _samples = new float[length];
        }

        interleavedStereoSamples[..length].CopyTo(_samples);
        _length = length;
        _position = 0;
    }

    public void SetSilence(int frameCount)
    {
        var sampleCount = Math.Max(0, frameCount) * 2;
        if (_samples.Length < sampleCount)
        {
            _samples = new float[sampleCount];
        }

        Array.Clear(_samples, 0, sampleCount);
        _length = sampleCount;
        _position = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var available = Math.Max(0, _length - _position);
        var copyCount = Math.Min(count, available);
        if (copyCount > 0)
        {
            Array.Copy(_samples, _position, buffer, offset, copyCount);
            _position += copyCount;
        }

        if (copyCount < count)
        {
            Array.Clear(buffer, offset + copyCount, count - copyCount);
        }

        return count;
    }
}
