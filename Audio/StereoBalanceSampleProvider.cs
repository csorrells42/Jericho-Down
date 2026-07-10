using NAudio.Wave;

namespace JerichoDown.Audio;

public sealed class StereoBalanceSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private double _balance;

    public StereoBalanceSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException("Stereo balance requires a stereo source.", nameof(source));
        }

        _source = source;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public double Balance
    {
        get => _balance;
        set => _balance = Math.Clamp(double.IsFinite(value) ? value : 0d, -1d, 1d);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var leftGain = Balance > 0d ? 1d - Balance : 1d;
        var rightGain = Balance < 0d ? 1d + Balance : 1d;
        if (Math.Abs(leftGain - 1d) < 0.0001d && Math.Abs(rightGain - 1d) < 0.0001d)
        {
            return read;
        }

        var end = offset + read - read % 2;
        for (var i = offset; i < end; i += 2)
        {
            buffer[i] = (float)Math.Clamp(buffer[i] * leftGain, -1d, 1d);
            buffer[i + 1] = (float)Math.Clamp(buffer[i + 1] * rightGain, -1d, 1d);
        }

        return read;
    }
}
