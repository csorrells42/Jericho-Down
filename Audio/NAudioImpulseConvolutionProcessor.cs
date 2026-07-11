using NAudio.Dsp;

namespace JerichoDown.Audio;

public sealed class NAudioImpulseConvolutionProcessor
{
    private readonly ImpulseResponseConvolution _convolver = new();
    private readonly double _sampleRate;
    private int _settingsRevision = -1;
    private bool _enabled;
    private float _mix;
    private float[] _impulse = [1f];
    private float[] _inputBuffer = [];
    private float[] _tailBuffer = [];
    private float[] _nextTailBuffer = [];

    public NAudioImpulseConvolutionProcessor(double sampleRate)
    {
        _sampleRate = Math.Clamp(double.IsFinite(sampleRate) ? sampleRate : 48000d, 8000d, 384000d);
    }

    public void UpdateFromSettings(VoiceProcessorSettings settings)
    {
        if (settings.SettingsRevision == _settingsRevision)
        {
            return;
        }

        _settingsRevision = settings.SettingsRevision;
        _enabled = settings.NAudioConvolutionEnabled;
        _mix = (float)Math.Clamp(double.IsFinite(settings.NAudioConvolutionMix) ? settings.NAudioConvolutionMix : 0.2d, 0d, 1d);
        _impulse = BuildImpulse(
            settings.NAudioConvolutionLengthMs,
            settings.NAudioConvolutionPreDelayMs,
            settings.NAudioConvolutionDecay);
        _tailBuffer = new float[Math.Max(0, _impulse.Length - 1)];
        _nextTailBuffer = new float[_tailBuffer.Length];
    }

    public void Transform(Span<float> samples)
    {
        if (!_enabled || _mix <= 0f || samples.Length == 0 || _impulse.Length <= 1)
        {
            return;
        }

        if (_inputBuffer.Length != samples.Length)
        {
            _inputBuffer = new float[samples.Length];
        }

        samples.CopyTo(_inputBuffer);
        var wetBuffer = _convolver.Convolve(_inputBuffer, _impulse);
        for (var i = 0; i < samples.Length; i++)
        {
            var tail = i < _tailBuffer.Length ? _tailBuffer[i] : 0f;
            var wet = Sanitize((i < wetBuffer.Length ? wetBuffer[i] : 0f) + tail);
            var dry = Sanitize(samples[i]);
            samples[i] = Sanitize(dry + (wet - dry) * _mix);
        }

        Array.Clear(_nextTailBuffer);
        for (var i = 0; i < _nextTailBuffer.Length; i++)
        {
            var shiftedTail = i + samples.Length < _tailBuffer.Length ? _tailBuffer[i + samples.Length] : 0f;
            var wetTail = i + samples.Length < wetBuffer.Length ? wetBuffer[i + samples.Length] : 0f;
            _nextTailBuffer[i] = Sanitize(shiftedTail + wetTail);
        }

        (_tailBuffer, _nextTailBuffer) = (_nextTailBuffer, _tailBuffer);
    }

    private float[] BuildImpulse(double lengthMs, double preDelayMs, double decay)
    {
        var impulseLength = Math.Max(2, (int)Math.Round(_sampleRate * Milliseconds(lengthMs, 10d, 160d) / 1000d));
        var preDelaySamples = Math.Clamp(
            (int)Math.Round(_sampleRate * Milliseconds(preDelayMs, 0d, 40d) / 1000d),
            0,
            impulseLength - 1);
        var decayAmount = Math.Clamp(double.IsFinite(decay) ? decay : 0.45d, 0.05d, 0.95d);
        var impulse = new float[impulseLength];
        impulse[0] = 1f;

        var reflectionCount = Math.Clamp((int)Math.Round(4d + decayAmount * 12d), 4, 16);
        for (var i = 0; i < reflectionCount; i++)
        {
            var positionRatio = (i + 1d) / (reflectionCount + 1d);
            var index = Math.Min(impulseLength - 1, preDelaySamples + (int)Math.Round((impulseLength - preDelaySamples - 1) * positionRatio));
            var sign = i % 2 == 0 ? 1f : -1f;
            impulse[index] += sign * (float)(Math.Pow(decayAmount, i + 1) * 0.45d);
        }

        _convolver.Normalize(impulse);
        return impulse;
    }

    private static double Milliseconds(double value, double minimum, double maximum)
    {
        return Math.Clamp(double.IsFinite(value) ? value : minimum, minimum, maximum);
    }

    private static float Sanitize(float sample)
    {
        return float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
    }
}
