using NAudio.Dmo;
using NAudio.Dmo.Effect;
using NAudio.Wave;

namespace JerichoDown.Audio;

public sealed class NAudioDmoEffectChain : IDisposable
{
    private readonly WaveFormat _waveFormat;
    private readonly List<IDmoRuntime> _runtimes = [];
    private int _settingsRevision = -1;
    private int _enabledMask = -1;
    private byte[] _byteBuffer = [];
    private bool _disposed;

    public NAudioDmoEffectChain(double sampleRate)
    {
        _waveFormat = new WaveFormat((int)Math.Clamp(double.IsFinite(sampleRate) ? sampleRate : 48000d, 8000d, 384000d), 16, 1);
    }

    public void UpdateFromSettings(VoiceProcessorSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        var enabledMask = EnabledMask(settings);
        if (enabledMask != _enabledMask)
        {
            Rebuild(enabledMask);
        }

        if (settings.SettingsRevision == _settingsRevision)
        {
            return;
        }

        _settingsRevision = settings.SettingsRevision;
        foreach (var runtime in _runtimes)
        {
            runtime.ApplySettings(settings);
        }
    }

    public void Transform(Span<float> samples)
    {
        if (_disposed || _runtimes.Count == 0 || samples.Length == 0)
        {
            return;
        }

        var byteCount = samples.Length * 2;
        if (_byteBuffer.Length < byteCount)
        {
            _byteBuffer = new byte[byteCount];
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var pcm = (short)Math.Clamp(Math.Round(Sanitize(samples[i]) * short.MaxValue), short.MinValue, short.MaxValue);
            _byteBuffer[i * 2] = (byte)(pcm & 0xFF);
            _byteBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        foreach (var runtime in _runtimes)
        {
            runtime.Process(_byteBuffer, byteCount);
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var pcm = (short)(_byteBuffer[i * 2] | (_byteBuffer[i * 2 + 1] << 8));
            samples[i] = Sanitize(pcm / 32768f);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearRuntimes();
        GC.SuppressFinalize(this);
    }

    ~NAudioDmoEffectChain()
    {
        Dispose();
    }

    private void Rebuild(int enabledMask)
    {
        _enabledMask = enabledMask;
        _settingsRevision = -1;
        ClearRuntimes();

        AddIfEnabled(enabledMask, 0, CreateRuntime<DmoChorus, DmoChorus.Params>(ConfigureChorus));
        AddIfEnabled(enabledMask, 1, CreateRuntime<DmoFlanger, DmoFlanger.Params>(ConfigureFlanger));
        AddIfEnabled(enabledMask, 2, CreateRuntime<DmoEcho, DmoEcho.Params>(ConfigureEcho));
        AddIfEnabled(enabledMask, 3, CreateRuntime<DmoDistortion, DmoDistortion.Params>(ConfigureDistortion));
        AddIfEnabled(enabledMask, 4, CreateRuntime<DmoCompressor, DmoCompressor.Params>(ConfigureCompressor));
        AddIfEnabled(enabledMask, 5, CreateRuntime<DmoParamEq, DmoParamEq.Params>(ConfigureParamEq));
        AddIfEnabled(enabledMask, 6, CreateRuntime<DmoGargle, DmoGargle.Params>(ConfigureGargle));
        AddIfEnabled(enabledMask, 7, CreateRuntime<DmoI3DL2Reverb, DmoI3DL2Reverb.Params>(ConfigureI3Dl2Reverb));
        AddIfEnabled(enabledMask, 8, CreateRuntime<DmoWavesReverb, DmoWavesReverb.Params>(ConfigureWavesReverb));
    }

    private DmoRuntime<TDmo, TParam>? CreateRuntime<TDmo, TParam>(Action<TParam, VoiceProcessorSettings> configure)
        where TDmo : IDmoEffector<TParam>, new()
    {
        try
        {
            var runtime = new DmoRuntime<TDmo, TParam>(_waveFormat, configure);
            return runtime.IsAvailable ? runtime : null;
        }
        catch
        {
            return null;
        }
    }

    private void AddIfEnabled(int enabledMask, int bit, IDmoRuntime? runtime)
    {
        if ((enabledMask & (1 << bit)) == 0)
        {
            runtime?.Dispose();
            return;
        }

        if (runtime is not null)
        {
            _runtimes.Add(runtime);
        }
    }

    private void ClearRuntimes()
    {
        foreach (var runtime in _runtimes)
        {
            runtime.Dispose();
        }

        _runtimes.Clear();
    }

    private static int EnabledMask(VoiceProcessorSettings settings)
    {
        var mask = 0;
        if (settings.NAudioDmoChorusEnabled) mask |= 1 << 0;
        if (settings.NAudioDmoFlangerEnabled) mask |= 1 << 1;
        if (settings.NAudioDmoEchoEnabled) mask |= 1 << 2;
        if (settings.NAudioDmoDistortionEnabled) mask |= 1 << 3;
        if (settings.NAudioDmoCompressorEnabled) mask |= 1 << 4;
        if (settings.NAudioDmoParamEqEnabled) mask |= 1 << 5;
        if (settings.NAudioDmoGargleEnabled) mask |= 1 << 6;
        if (settings.NAudioDmoI3DL2ReverbEnabled) mask |= 1 << 7;
        if (settings.NAudioDmoWavesReverbEnabled) mask |= 1 << 8;
        return mask;
    }

    private static void ConfigureChorus(DmoChorus.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.WetDryMix = Float(settings.NAudioDmoChorusWetDryMix, 0f, 100f);
        parameters.Depth = Float(settings.NAudioDmoChorusDepth, 0f, 100f);
        parameters.FeedBack = Float(settings.NAudioDmoChorusFeedback, -99f, 99f);
        parameters.Frequency = Float(settings.NAudioDmoChorusFrequencyHz, 0f, 10f);
        parameters.WaveForm = EnumValue<ChorusWaveForm>(settings.NAudioDmoChorusWaveForm);
        parameters.Delay = Float(settings.NAudioDmoChorusDelayMs, 0f, 20f);
        parameters.Phase = EnumValue<ChorusPhase>(settings.NAudioDmoChorusPhase);
    }

    private static void ConfigureFlanger(DmoFlanger.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.WetDryMix = Float(settings.NAudioDmoFlangerWetDryMix, 0f, 100f);
        parameters.Depth = Float(settings.NAudioDmoFlangerDepth, 0f, 100f);
        parameters.FeedBack = Float(settings.NAudioDmoFlangerFeedback, -99f, 99f);
        parameters.Frequency = Float(settings.NAudioDmoFlangerFrequencyHz, 0f, 10f);
        parameters.WaveForm = EnumValue<FlangerWaveForm>(settings.NAudioDmoFlangerWaveForm);
        parameters.Delay = Float(settings.NAudioDmoFlangerDelayMs, 0f, 4f);
        parameters.Phase = EnumValue<FlangerPhase>(settings.NAudioDmoFlangerPhase);
    }

    private static void ConfigureEcho(DmoEcho.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.WetDryMix = Float(settings.NAudioDmoEchoWetDryMix, 0f, 100f);
        parameters.FeedBack = Float(settings.NAudioDmoEchoFeedback, 0f, 100f);
        parameters.LeftDelay = Float(settings.NAudioDmoEchoLeftDelayMs, 1f, 2000f);
        parameters.RightDelay = Float(settings.NAudioDmoEchoRightDelayMs, 1f, 2000f);
        parameters.PanDelay = EnumValue<EchoPanDelay>(settings.NAudioDmoEchoPanDelay);
    }

    private static void ConfigureDistortion(DmoDistortion.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.Gain = Float(settings.NAudioDmoDistortionGainDb, -60f, 0f);
        parameters.Edge = Float(settings.NAudioDmoDistortionEdge, 0f, 100f);
        parameters.PostEqCenterFrequency = Float(settings.NAudioDmoDistortionPostEqCenterFrequencyHz, 100f, 8000f);
        parameters.PostEqBandWidth = Float(settings.NAudioDmoDistortionPostEqBandwidthHz, 100f, 8000f);
        parameters.PreLowPassCutoff = Float(settings.NAudioDmoDistortionPreLowPassCutoffHz, 100f, 8000f);
    }

    private static void ConfigureCompressor(DmoCompressor.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.Gain = Float(settings.NAudioDmoCompressorGainDb, -60f, 60f);
        parameters.Attack = Float(settings.NAudioDmoCompressorAttackMs, 0.01f, 500f);
        parameters.Release = Float(settings.NAudioDmoCompressorReleaseMs, 50f, 3000f);
        parameters.Threshold = Float(settings.NAudioDmoCompressorThresholdDb, -60f, 0f);
        parameters.Ratio = Float(settings.NAudioDmoCompressorRatio, 1f, 100f);
        parameters.PreDelay = Float(settings.NAudioDmoCompressorPreDelayMs, 0f, 4f);
    }

    private static void ConfigureParamEq(DmoParamEq.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.Center = Float(settings.NAudioDmoParamEqCenterFrequencyHz, 80f, 16000f);
        parameters.BandWidth = Float(settings.NAudioDmoParamEqBandwidthHz, 1f, 36f);
        parameters.Gain = Float(settings.NAudioDmoParamEqGainDb, -15f, 15f);
    }

    private static void ConfigureGargle(DmoGargle.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.RateHz = (uint)Math.Clamp((int)Math.Round(Finite(settings.NAudioDmoGargleRateHz, 20d)), 1, 1000);
        parameters.WaveShape = EnumValue<GargleWaveShape>(settings.NAudioDmoGargleWaveShape);
    }

    private static void ConfigureI3Dl2Reverb(DmoI3DL2Reverb.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.Room = Int(settings.NAudioDmoI3DL2Room, -10000, 0);
        parameters.RoomHf = Int(settings.NAudioDmoI3DL2RoomHf, -10000, 0);
        parameters.RoomRollOffFactor = Float(settings.NAudioDmoI3DL2RoomRolloffFactor, 0f, 10f);
        parameters.DecayTime = Float(settings.NAudioDmoI3DL2DecayTimeSeconds, 0.1f, 20f);
        parameters.DecayHfRatio = Float(settings.NAudioDmoI3DL2DecayHfRatio, 0.1f, 2f);
        parameters.Reflections = Int(settings.NAudioDmoI3DL2Reflections, -10000, 1000);
        parameters.ReflectionsDelay = Float(settings.NAudioDmoI3DL2ReflectionsDelaySeconds, 0f, 0.3f);
        parameters.Reverb = Int(settings.NAudioDmoI3DL2Reverb, -10000, 2000);
        parameters.ReverbDelay = Float(settings.NAudioDmoI3DL2ReverbDelaySeconds, 0f, 0.1f);
        parameters.Diffusion = Float(settings.NAudioDmoI3DL2Diffusion, 0f, 100f);
        parameters.Density = Float(settings.NAudioDmoI3DL2Density, 0f, 100f);
        parameters.HfReference = Float(settings.NAudioDmoI3DL2HfReferenceHz, 20f, 20000f);
        parameters.Quality = Int(settings.NAudioDmoI3DL2Quality, 0, 3);
    }

    private static void ConfigureWavesReverb(DmoWavesReverb.Params parameters, VoiceProcessorSettings settings)
    {
        parameters.InGain = Float(settings.NAudioDmoWavesReverbInGainDb, -96f, 0f);
        parameters.ReverbMix = Float(settings.NAudioDmoWavesReverbMixDb, -96f, 0f);
        parameters.ReverbTime = Float(settings.NAudioDmoWavesReverbTimeMs, 0.001f, 3000f);
        parameters.HighFreqRtRatio = Float(settings.NAudioDmoWavesReverbHighFreqRtRatio, 0.001f, 0.999f);
    }

    private static TEnum EnumValue<TEnum>(double value)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var index = Math.Clamp((int)Math.Round(Finite(value, 0d)), 0, values.Length - 1);
        return values[index];
    }

    private static int Int(double value, int minimum, int maximum)
    {
        return Math.Clamp((int)Math.Round(Finite(value, minimum)), minimum, maximum);
    }

    private static float Float(double value, float minimum, float maximum)
    {
        return (float)Math.Clamp(Finite(value, minimum), minimum, maximum);
    }

    private static double Finite(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }

    private static float Sanitize(float sample)
    {
        return float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
    }

    private interface IDmoRuntime : IDisposable
    {
        void ApplySettings(VoiceProcessorSettings settings);

        void Process(byte[] buffer, int byteCount);
    }

    private sealed class DmoRuntime<TDmo, TParam> : IDmoRuntime
        where TDmo : IDmoEffector<TParam>, new()
    {
        private readonly TDmo _effector;
        private readonly Action<TParam, VoiceProcessorSettings> _configure;
        private bool _streamingResourcesAllocated;
        private bool _failed;

        public DmoRuntime(WaveFormat waveFormat, Action<TParam, VoiceProcessorSettings> configure)
        {
            _configure = configure;
            _effector = new TDmo();
            if (_effector.MediaObject is null || _effector.MediaObjectInPlace is null)
            {
                return;
            }

            if (!_effector.MediaObject.SupportsInputWaveFormat(0, waveFormat)
                || !_effector.MediaObject.SupportsOutputWaveFormat(0, waveFormat))
            {
                return;
            }

            _effector.MediaObject.AllocateStreamingResources();
            _streamingResourcesAllocated = true;
            _effector.MediaObject.SetInputWaveFormat(0, waveFormat);
            _effector.MediaObject.SetOutputWaveFormat(0, waveFormat);
        }

        public bool IsAvailable => !_failed
            && _effector.MediaObject is not null
            && _effector.MediaObjectInPlace is not null
            && _streamingResourcesAllocated;

        public void ApplySettings(VoiceProcessorSettings settings)
        {
            if (!IsAvailable)
            {
                return;
            }

            TryDmo(() => _configure(_effector.EffectParams, settings));
        }

        public void Process(byte[] buffer, int byteCount)
        {
            if (!IsAvailable)
            {
                return;
            }

            TryDmo(() => _effector.MediaObjectInPlace.Process(byteCount, 0, buffer, 0, DmoInPlaceProcessFlags.Normal));
        }

        public void Dispose()
        {
            if (_streamingResourcesAllocated && _effector.MediaObject is not null)
            {
                TryDmo(() => _effector.MediaObject.FreeStreamingResources());
            }

            _streamingResourcesAllocated = false;
            _effector.Dispose();
        }

        private void TryDmo(Action action)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                action();
            }
            catch
            {
                _failed = true;
            }
        }
    }
}
