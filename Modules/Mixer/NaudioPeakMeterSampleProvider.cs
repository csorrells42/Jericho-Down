using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JerichoDown.Modules.Mixer;

public sealed class NaudioPeakMeterSampleProvider : ISampleProvider
{
    private readonly MeteringSampleProvider _meter;
    private double _peakLevel;

    public NaudioPeakMeterSampleProvider(ISampleProvider source, int samplesPerNotification)
    {
        ArgumentNullException.ThrowIfNull(source);

        _meter = new MeteringSampleProvider(source)
        {
            SamplesPerNotification = Math.Max(1, samplesPerNotification)
        };
        _meter.StreamVolume += MeterStreamVolume;
    }

    public WaveFormat WaveFormat => _meter.WaveFormat;

    public double PeakLevel => System.Threading.Volatile.Read(ref _peakLevel);

    public int Read(float[] buffer, int offset, int count)
    {
        return _meter.Read(buffer, offset, count);
    }

    private void MeterStreamVolume(object? sender, StreamVolumeEventArgs e)
    {
        var peak = 0d;
        foreach (var sampleValue in e.MaxSampleValues)
        {
            if (float.IsFinite(sampleValue))
            {
                peak = Math.Max(peak, Math.Abs(sampleValue));
            }
        }

        System.Threading.Volatile.Write(ref _peakLevel, Math.Clamp(peak, 0d, 1d));
    }
}
