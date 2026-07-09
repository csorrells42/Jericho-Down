using NAudio.Wave;

namespace JerichoDown.Audio;

public sealed class StereoPanSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _monoScratch = [];
    private double _pan;

    public StereoPanSampleProvider(ISampleProvider source)
    {
        if (source.WaveFormat.Channels != 1)
        {
            throw new ArgumentException("Stereo pan provider expects a mono source.", nameof(source));
        }

        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public double Pan
    {
        get => _pan;
        set => _pan = Math.Clamp(double.IsFinite(value) ? value : 0d, -1d, 1d);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var frameCount = count / 2;
        if (frameCount <= 0)
        {
            return 0;
        }

        if (_monoScratch.Length < frameCount)
        {
            _monoScratch = new float[frameCount];
        }

        var readFrames = _source.Read(_monoScratch, 0, frameCount);
        var panPosition = (Pan + 1d) * Math.PI / 4d;
        var leftGain = Math.Cos(panPosition);
        var rightGain = Math.Sin(panPosition);
        var write = offset;
        for (var i = 0; i < readFrames; i++)
        {
            var sample = Math.Clamp(float.IsFinite(_monoScratch[i]) ? _monoScratch[i] : 0f, -1f, 1f);
            buffer[write++] = (float)(sample * leftGain);
            buffer[write++] = (float)(sample * rightGain);
        }

        var writtenSamples = readFrames * 2;
        if (writtenSamples < count)
        {
            Array.Clear(buffer, offset + writtenSamples, count - writtenSamples);
        }

        return count;
    }
}
