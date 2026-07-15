using NAudio.Dsp;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed class NAudioBiQuadFilterRack
{
    private readonly float _sampleRate;
    private int _settingsRevision = -1;
    private BiQuadFilter? _lowPass;
    private BiQuadFilter? _highPass;
    private BiQuadFilter? _bandPassPeak;
    private BiQuadFilter? _bandPassSkirt;
    private BiQuadFilter? _notch;
    private BiQuadFilter? _allPass;
    private BiQuadFilter? _peakingEq;
    private BiQuadFilter? _lowShelf;
    private BiQuadFilter? _highShelf;

    public NAudioBiQuadFilterRack(double sampleRate)
    {
        _sampleRate = (float)Math.Clamp(sampleRate, 8000d, 384000d);
    }

    public void UpdateFromSettings(VoiceProcessorSettings settings)
    {
        if (settings.SettingsRevision == _settingsRevision)
        {
            return;
        }

        _settingsRevision = settings.SettingsRevision;
        _lowPass = settings.NAudioLowPassEnabled
            ? BiQuadFilter.LowPassFilter(_sampleRate, Frequency(settings.NAudioLowPassFrequencyHz), Q(settings.NAudioLowPassQ))
            : null;
        _highPass = settings.NAudioHighPassEnabled
            ? BiQuadFilter.HighPassFilter(_sampleRate, Frequency(settings.NAudioHighPassFrequencyHz), Q(settings.NAudioHighPassQ))
            : null;
        _bandPassPeak = settings.NAudioBandPassPeakEnabled
            ? BiQuadFilter.BandPassFilterConstantPeakGain(_sampleRate, Frequency(settings.NAudioBandPassPeakFrequencyHz), Q(settings.NAudioBandPassPeakQ))
            : null;
        _bandPassSkirt = settings.NAudioBandPassSkirtEnabled
            ? BiQuadFilter.BandPassFilterConstantSkirtGain(_sampleRate, Frequency(settings.NAudioBandPassSkirtFrequencyHz), Q(settings.NAudioBandPassSkirtQ))
            : null;
        _notch = settings.NAudioNotchEnabled
            ? BiQuadFilter.NotchFilter(_sampleRate, Frequency(settings.NAudioNotchFrequencyHz), Q(settings.NAudioNotchQ))
            : null;
        _allPass = settings.NAudioAllPassEnabled
            ? BiQuadFilter.AllPassFilter(_sampleRate, Frequency(settings.NAudioAllPassFrequencyHz), Q(settings.NAudioAllPassQ))
            : null;
        _peakingEq = settings.NAudioPeakingEqEnabled && Math.Abs(settings.NAudioPeakingEqGainDb) > 0.01d
            ? BiQuadFilter.PeakingEQ(_sampleRate, Frequency(settings.NAudioPeakingEqFrequencyHz), Q(settings.NAudioPeakingEqQ), Gain(settings.NAudioPeakingEqGainDb))
            : null;
        _lowShelf = settings.NAudioLowShelfEnabled && Math.Abs(settings.NAudioLowShelfGainDb) > 0.01d
            ? BiQuadFilter.LowShelf(_sampleRate, Frequency(settings.NAudioLowShelfFrequencyHz), Slope(settings.NAudioLowShelfSlope), Gain(settings.NAudioLowShelfGainDb))
            : null;
        _highShelf = settings.NAudioHighShelfEnabled && Math.Abs(settings.NAudioHighShelfGainDb) > 0.01d
            ? BiQuadFilter.HighShelf(_sampleRate, Frequency(settings.NAudioHighShelfFrequencyHz), Slope(settings.NAudioHighShelfSlope), Gain(settings.NAudioHighShelfGainDb))
            : null;
    }

    public double Transform(double sample)
    {
        var value = (float)Math.Clamp(double.IsFinite(sample) ? sample : 0d, -4d, 4d);
        value = Transform(_highPass, value);
        value = Transform(_lowPass, value);
        value = Transform(_bandPassPeak, value);
        value = Transform(_bandPassSkirt, value);
        value = Transform(_notch, value);
        value = Transform(_allPass, value);
        value = Transform(_peakingEq, value);
        value = Transform(_lowShelf, value);
        value = Transform(_highShelf, value);
        return double.IsFinite(value) ? value : 0d;
    }

    private static float Transform(BiQuadFilter? filter, float sample)
    {
        return filter is null ? sample : filter.Transform(sample);
    }

    private float Frequency(double frequencyHz)
    {
        return (float)Math.Clamp(double.IsFinite(frequencyHz) ? frequencyHz : 1000d, 20d, _sampleRate * 0.45d);
    }

    private static float Q(double q)
    {
        return (float)Math.Clamp(double.IsFinite(q) ? q : 0.707d, 0.1d, 48d);
    }

    private static float Slope(double slope)
    {
        return (float)Math.Clamp(double.IsFinite(slope) ? slope : 0.9d, 0.1d, 4d);
    }

    private static float Gain(double gainDb)
    {
        return (float)Math.Clamp(double.IsFinite(gainDb) ? gainDb : 0d, -24d, 24d);
    }
}
