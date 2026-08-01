using NAudio.Dsp;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed class NAudioBiQuadFilterRack
{
    private readonly float _sampleRate;
    private BiQuadFilter? _lowPass;
    private BiQuadFilter? _highPass;
    private BiQuadFilter? _bandPassPeak;
    private BiQuadFilter? _bandPassSkirt;
    private BiQuadFilter? _notch;
    private BiQuadFilter? _allPass;
    private BiQuadFilter? _peakingEq;
    private BiQuadFilter? _lowShelf;
    private BiQuadFilter? _highShelf;

    private FilterKey _lowPassKey;
    private FilterKey _highPassKey;
    private FilterKey _bandPassPeakKey;
    private FilterKey _bandPassSkirtKey;
    private FilterKey _notchKey;
    private FilterKey _allPassKey;
    private FilterKey _peakingEqKey;
    private FilterKey _lowShelfKey;
    private FilterKey _highShelfKey;

    public NAudioBiQuadFilterRack(double sampleRate)
    {
        _sampleRate = (float)Math.Clamp(sampleRate, 8000d, 384000d);
    }

    public void UpdateFromSettings(VoiceProcessorSettings settings)
    {
        // Each filter only rebuilds (and loses its internal delay-line state) when its own
        // parameters change - not whenever any unrelated setting elsewhere in the chain is
        // touched, which previously reset every enabled filter and clicked on every knob move.
        UpdateFilter(
            ref _lowPass,
            ref _lowPassKey,
            new FilterKey(settings.NAudioLowPassEnabled, settings.NAudioLowPassFrequencyHz, settings.NAudioLowPassQ, 0d, 0d),
            key => BiQuadFilter.LowPassFilter(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q)));

        UpdateFilter(
            ref _highPass,
            ref _highPassKey,
            new FilterKey(settings.NAudioHighPassEnabled, settings.NAudioHighPassFrequencyHz, settings.NAudioHighPassQ, 0d, 0d),
            key => BiQuadFilter.HighPassFilter(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q)));

        UpdateFilter(
            ref _bandPassPeak,
            ref _bandPassPeakKey,
            new FilterKey(settings.NAudioBandPassPeakEnabled, settings.NAudioBandPassPeakFrequencyHz, settings.NAudioBandPassPeakQ, 0d, 0d),
            key => BiQuadFilter.BandPassFilterConstantPeakGain(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q)));

        UpdateFilter(
            ref _bandPassSkirt,
            ref _bandPassSkirtKey,
            new FilterKey(settings.NAudioBandPassSkirtEnabled, settings.NAudioBandPassSkirtFrequencyHz, settings.NAudioBandPassSkirtQ, 0d, 0d),
            key => BiQuadFilter.BandPassFilterConstantSkirtGain(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q)));

        UpdateFilter(
            ref _notch,
            ref _notchKey,
            new FilterKey(settings.NAudioNotchEnabled, settings.NAudioNotchFrequencyHz, settings.NAudioNotchQ, 0d, 0d),
            key => BiQuadFilter.NotchFilter(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q)));

        UpdateFilter(
            ref _allPass,
            ref _allPassKey,
            new FilterKey(settings.NAudioAllPassEnabled, settings.NAudioAllPassFrequencyHz, settings.NAudioAllPassQ, 0d, 0d),
            key => BiQuadFilter.AllPassFilter(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q)));

        UpdateFilter(
            ref _peakingEq,
            ref _peakingEqKey,
            new FilterKey(
                settings.NAudioPeakingEqEnabled && Math.Abs(settings.NAudioPeakingEqGainDb) > 0.01d,
                settings.NAudioPeakingEqFrequencyHz,
                settings.NAudioPeakingEqQ,
                settings.NAudioPeakingEqGainDb,
                0d),
            key => BiQuadFilter.PeakingEQ(_sampleRate, Frequency(key.FrequencyHz), Q(key.Q), Gain(key.GainDb)));

        UpdateFilter(
            ref _lowShelf,
            ref _lowShelfKey,
            new FilterKey(
                settings.NAudioLowShelfEnabled && Math.Abs(settings.NAudioLowShelfGainDb) > 0.01d,
                settings.NAudioLowShelfFrequencyHz,
                settings.NAudioLowShelfSlope,
                settings.NAudioLowShelfGainDb,
                0d),
            key => BiQuadFilter.LowShelf(_sampleRate, Frequency(key.FrequencyHz), Slope(key.Q), Gain(key.GainDb)));

        UpdateFilter(
            ref _highShelf,
            ref _highShelfKey,
            new FilterKey(
                settings.NAudioHighShelfEnabled && Math.Abs(settings.NAudioHighShelfGainDb) > 0.01d,
                settings.NAudioHighShelfFrequencyHz,
                settings.NAudioHighShelfSlope,
                settings.NAudioHighShelfGainDb,
                0d),
            key => BiQuadFilter.HighShelf(_sampleRate, Frequency(key.FrequencyHz), Slope(key.Q), Gain(key.GainDb)));
    }

    private static void UpdateFilter(ref BiQuadFilter? filter, ref FilterKey currentKey, FilterKey newKey, Func<FilterKey, BiQuadFilter> factory)
    {
        if (newKey.Equals(currentKey))
        {
            return;
        }

        currentKey = newKey;
        filter = newKey.Enabled ? factory(newKey) : null;
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

    private readonly record struct FilterKey(bool Enabled, double FrequencyHz, double Q, double GainDb, double Slope);
}
