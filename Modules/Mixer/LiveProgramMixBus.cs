using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JerichoDown.Modules.Mixer;

public sealed class LiveProgramMixBus
{
    private readonly MixingSampleProvider _mixer;

    public LiveProgramMixBus(int sampleRate)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Math.Max(8000, sampleRate), 2);
        _mixer = new MixingSampleProvider(WaveFormat)
        {
            ReadFully = true
        };
    }

    public WaveFormat WaveFormat { get; }

    public int InputCount { get; private set; }

    public void AddMicInput(ISampleProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.WaveFormat.SampleRate != WaveFormat.SampleRate || provider.WaveFormat.Channels != WaveFormat.Channels)
        {
            throw new ArgumentException("Live program mix inputs must already be stereo at the bus sample rate.", nameof(provider));
        }

        _mixer.AddMixerInput(provider);
        InputCount++;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var read = _mixer.Read(buffer, offset, count);
        if (read < count)
        {
            Array.Clear(buffer, offset + Math.Max(0, read), count - Math.Max(0, read));
        }

        return count;
    }
}
