using NAudio.Dsp;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed class NAudioEnvelopeGeneratorProcessor
{
    private readonly EnvelopeGenerator _envelopeGenerator = new();
    private readonly double _sampleRate;
    private readonly double _detectorReleaseCoefficient;
    private EnvelopeKey _envelopeKey;
    private bool _envelopeInitialized;
    private bool _enabled;
    private double _triggerThreshold;
    private double _detectorEnvelope;
    private double _mix;
    private bool _gateOpen;

    public NAudioEnvelopeGeneratorProcessor(double sampleRate)
    {
        _sampleRate = Math.Clamp(double.IsFinite(sampleRate) ? sampleRate : 48000d, 8000d, 384000d);
        _detectorReleaseCoefficient = Math.Exp(-1d / (_sampleRate * 0.015d));
    }

    public void UpdateFromSettings(VoiceProcessorSettings settings)
    {
        _enabled = settings.NAudioEnvelopeEnabled;
        _mix = Math.Clamp(double.IsFinite(settings.NAudioEnvelopeMix) ? settings.NAudioEnvelopeMix : 1d, 0d, 1d);
        _triggerThreshold = DbToLinear(Math.Clamp(
            double.IsFinite(settings.NAudioEnvelopeTriggerThresholdDb) ? settings.NAudioEnvelopeTriggerThresholdDb : -48d,
            -96d,
            0d));

        var envelopeKey = new EnvelopeKey(
            settings.NAudioEnvelopeAttackMs,
            settings.NAudioEnvelopeDecayMs,
            settings.NAudioEnvelopeReleaseMs,
            settings.NAudioEnvelopeSustainLevel);
        if (_envelopeInitialized && envelopeKey.Equals(_envelopeKey))
        {
            // Only the enable flag, mix, or trigger threshold changed - don't reset the
            // envelope generator's in-flight attack/decay/release/gate state mid-note.
            return;
        }

        _envelopeInitialized = true;
        _envelopeKey = envelopeKey;

        var attackSamples = MillisecondsToSamples(envelopeKey.AttackMs, 0.1d, 5000d);
        var decaySamples = MillisecondsToSamples(envelopeKey.DecayMs, 0.1d, 5000d);
        var releaseSamples = MillisecondsToSamples(envelopeKey.ReleaseMs, 1d, 10000d);
        var sustain = Math.Clamp(double.IsFinite(envelopeKey.SustainLevel) ? envelopeKey.SustainLevel : 0.75d, 0d, 1d);

        _envelopeGenerator.AttackRate = (float)attackSamples;
        _envelopeGenerator.DecayRate = (float)decaySamples;
        _envelopeGenerator.SustainLevel = (float)sustain;
        _envelopeGenerator.ReleaseRate = (float)releaseSamples;
        _envelopeGenerator.Reset();
        _detectorEnvelope = 0d;
        _gateOpen = false;
    }

    public double Transform(double sample)
    {
        if (!_enabled || _mix <= 0d)
        {
            return sample;
        }

        var level = Math.Abs(double.IsFinite(sample) ? sample : 0d);
        _detectorEnvelope = level > _detectorEnvelope
            ? level
            : _detectorEnvelope * _detectorReleaseCoefficient;
        var shouldGate = _detectorEnvelope >= _triggerThreshold;
        if (shouldGate != _gateOpen)
        {
            _gateOpen = shouldGate;
            _envelopeGenerator.Gate(shouldGate);
        }

        var envelope = Math.Clamp(_envelopeGenerator.Process(), 0f, 1f);
        var gain = 1d + (envelope - 1d) * _mix;
        var output = sample * gain;
        return double.IsFinite(output) ? output : 0d;
    }

    private double MillisecondsToSamples(double milliseconds, double minimum, double maximum)
    {
        return Math.Max(1d, _sampleRate * Math.Clamp(double.IsFinite(milliseconds) ? milliseconds : minimum, minimum, maximum) / 1000d);
    }

    private static double DbToLinear(double db)
    {
        return Math.Pow(10d, db / 20d);
    }

    private readonly record struct EnvelopeKey(double AttackMs, double DecayMs, double ReleaseMs, double SustainLevel);
}
