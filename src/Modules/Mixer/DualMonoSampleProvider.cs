using NAudio.Wave;

namespace JerichoDown.Modules.Mixer;

public sealed class DualMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _monoScratch = [];

    public DualMonoSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels != 1)
        {
            throw new ArgumentException("Dual-mono provider expects a mono source.", nameof(source));
        }

        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

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
        var write = offset;
        for (var i = 0; i < readFrames; i++)
        {
            var sample = Math.Clamp(float.IsFinite(_monoScratch[i]) ? _monoScratch[i] : 0f, -1f, 1f);
            buffer[write++] = sample;
            buffer[write++] = sample;
        }

        var writtenSamples = readFrames * 2;
        if (writtenSamples < count)
        {
            Array.Clear(buffer, offset + writtenSamples, count - writtenSamples);
        }

        return count;
    }
}
