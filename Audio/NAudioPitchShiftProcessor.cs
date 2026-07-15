using NAudio.Dsp;
using JerichoDown.Modules.Audio.Dsp;

namespace JerichoDown.Audio;

public sealed class NAudioPitchShiftProcessor
{
    private readonly SmbPitchShifter _pitchShifter = new();
    private readonly float _sampleRate;
    private int _settingsRevision = -1;
    private bool _enabled;
    private float _pitchRatio = 1f;
    private int _fftFrameSize = 1024;
    private int _oversampling = 4;
    private float _mix = 1f;
    private float[] _workBuffer = [];

    public NAudioPitchShiftProcessor(double sampleRate)
    {
        _sampleRate = (float)Math.Clamp(double.IsFinite(sampleRate) ? sampleRate : 48000d, 8000d, 384000d);
    }

    public void UpdateFromSettings(VoiceProcessorSettings settings)
    {
        if (settings.SettingsRevision == _settingsRevision)
        {
            return;
        }

        _settingsRevision = settings.SettingsRevision;
        _enabled = settings.NAudioPitchShiftEnabled;
        _pitchRatio = (float)Math.Pow(2d, Semitones(settings.NAudioPitchShiftSemitones) / 12d);
        _fftFrameSize = FftFrameSize(settings.NAudioPitchShiftFftSize);
        _oversampling = Oversampling(settings.NAudioPitchShiftOversampling);
        _mix = (float)Math.Clamp(double.IsFinite(settings.NAudioPitchShiftMix) ? settings.NAudioPitchShiftMix : 1d, 0d, 1d);
    }

    public void Transform(Span<float> samples)
    {
        if (!_enabled || samples.Length == 0 || Math.Abs(_pitchRatio - 1f) < 0.0001f || _mix <= 0f)
        {
            return;
        }

        if (_workBuffer.Length < samples.Length)
        {
            _workBuffer = new float[samples.Length];
        }

        samples.CopyTo(_workBuffer);
        _pitchShifter.PitchShift(
            _pitchRatio,
            samples.Length,
            _fftFrameSize,
            _oversampling,
            _sampleRate,
            _workBuffer);

        for (var i = 0; i < samples.Length; i++)
        {
            var wet = Sanitize(_workBuffer[i]);
            var dry = Sanitize(samples[i]);
            samples[i] = Sanitize(dry + (wet - dry) * _mix);
        }
    }

    private static double Semitones(double semitones)
    {
        return Math.Clamp(double.IsFinite(semitones) ? semitones : 0d, -24d, 24d);
    }

    private static int FftFrameSize(double frameSize)
    {
        var requested = (int)Math.Round(Math.Clamp(double.IsFinite(frameSize) ? frameSize : 1024d, 256d, 4096d));
        var frame = 256;
        while (frame < requested && frame < 4096)
        {
            frame *= 2;
        }

        return frame;
    }

    private static int Oversampling(double oversampling)
    {
        return (int)Math.Round(Math.Clamp(double.IsFinite(oversampling) ? oversampling : 4d, 2d, 32d));
    }

    private static float Sanitize(float sample)
    {
        return float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
    }
}
