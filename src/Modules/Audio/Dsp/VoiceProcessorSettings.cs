using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed class VoiceProcessorSettings : INotifyPropertyChanged
{
    private readonly object _equalizerLock = new();
    private readonly double[] _equalizerGainsDb = new double[20];
    private int _settingsRevision;
    private int _equalizerRevision;
    private double _inputTrimDb;
    private bool _highPassEnabled = true;
    private double _highPassFrequencyHz = 80;
    private bool _lowPassEnabled;
    private double _lowPassFrequencyHz = 16000;
    private bool _humRemovalEnabled;
    private double _humRemovalFrequencyHz = 60;
    private bool _notchFilterEnabled;
    private double _notchFilterFrequencyHz = 2800;
    private double _notchFilterDepthDb = 18;
    private double _notchFilterQ = 16;
    private bool _parametricEqEnabled;
    private double _parametricEqFrequencyHz = 1000;
    private double _parametricEqGainDb;
    private double _parametricEqQ = 1.2;
    private bool _shelfEqEnabled;
    private double _lowShelfFrequencyHz = 160;
    private double _lowShelfGainDb;
    private double _highShelfFrequencyHz = 8000;
    private double _highShelfGainDb;
    private bool _noiseGateEnabled = true;
    private double _noiseGateThresholdDb = -48;
    private double _noiseGateAttackMs = 5;
    private double _noiseGateHoldMs = 90;
    private double _noiseGateReleaseMs = 140;
    private double _noiseGateRangeDb = 28;
    private bool _expanderEnabled = true;
    private double _expanderThresholdDb = -42;
    private double _expanderRatio = 1.8;
    private double _expanderRangeDb = 12;
    private double _expanderAttackMs = 8;
    private double _expanderHoldMs = 60;
    private double _expanderReleaseMs = 220;
    private bool _noiseSuppressionEnabled = true;
    private double _noiseSuppressionAmountDb = 6;
    private double _noiseSuppressionSensitivity = 4;
    private bool _echoReducerEnabled = true;
    private double _echoReducerAmountDb = 4;
    private double _echoReducerSensitivity = 5;
    private bool _compressorEnabled = true;
    private double _compressorThresholdDb = -18;
    private double _compressorRatio = 3;
    private double _compressorAttackMs = 12;
    private double _compressorReleaseMs = 140;
    private double _compressorKneeDb = 6;
    private bool _deEsserEnabled = true;
    private double _deEsserAmountDb = 3;
    private double _deEsserFrequencyHz = 5200;
    private double _deEsserThresholdDb = -34;
    private double _deEsserRangeDb = 9;
    private bool _breathReducerEnabled;
    private double _breathReducerAmountDb = 6;
    private double _breathReducerSensitivity = 5;
    private bool _presenceEnhancerEnabled = true;
    private double _presenceEnhancerAmountDb = 2;
    private double _presenceEnhancerFrequencyHz = 3000;
    private double _presenceEnhancerWidthHz = 2600;
    private bool _saturationEnabled;
    private double _saturationAmount = 2;
    private bool _dePopperEnabled = true;
    private double _dePopperAmountDb = 6;
    private double _dePopperFrequencyHz = 180;
    private double _dePopperThresholdDb = -28;
    private double _makeupGainDb = 2;
    private bool _limiterEnabled = true;
    private double _limiterCeilingDb = -1;
    private bool _limiterSoftClipEnabled = true;
    private double _limiterSoftClipDriveDb = 1.5;
    private bool _limiterLookaheadEnabled = true;
    private double _limiterLookaheadMs = 3;
    private double _limiterReleaseMs = 60;
    private bool _naudioLowPassEnabled;
    private double _naudioLowPassFrequencyHz = 12000;
    private double _naudioLowPassQ = 0.707;
    private bool _naudioHighPassEnabled;
    private double _naudioHighPassFrequencyHz = 80;
    private double _naudioHighPassQ = 0.707;
    private bool _naudioBandPassPeakEnabled;
    private double _naudioBandPassPeakFrequencyHz = 1000;
    private double _naudioBandPassPeakQ = 1.2;
    private bool _naudioBandPassSkirtEnabled;
    private double _naudioBandPassSkirtFrequencyHz = 1000;
    private double _naudioBandPassSkirtQ = 1.2;
    private bool _naudioNotchEnabled;
    private double _naudioNotchFrequencyHz = 2800;
    private double _naudioNotchQ = 16;
    private bool _naudioAllPassEnabled;
    private double _naudioAllPassFrequencyHz = 1000;
    private double _naudioAllPassQ = 0.707;
    private bool _naudioPeakingEqEnabled;
    private double _naudioPeakingEqFrequencyHz = 1000;
    private double _naudioPeakingEqQ = 1.2;
    private double _naudioPeakingEqGainDb;
    private bool _naudioLowShelfEnabled;
    private double _naudioLowShelfFrequencyHz = 160;
    private double _naudioLowShelfSlope = 0.9;
    private double _naudioLowShelfGainDb;
    private bool _naudioHighShelfEnabled;
    private double _naudioHighShelfFrequencyHz = 8000;
    private double _naudioHighShelfSlope = 0.9;
    private double _naudioHighShelfGainDb;
    private bool _naudioPitchShiftEnabled;
    private double _naudioPitchShiftSemitones;
    private double _naudioPitchShiftFftSize = 1024;
    private double _naudioPitchShiftOversampling = 4;
    private double _naudioPitchShiftMix = 1;
    private bool _naudioConvolutionEnabled;
    private double _naudioConvolutionLengthMs = 80;
    private double _naudioConvolutionPreDelayMs = 8;
    private double _naudioConvolutionDecay = 0.45;
    private double _naudioConvolutionMix = 0.2;
    private bool _naudioEnvelopeEnabled;
    private double _naudioEnvelopeTriggerThresholdDb = -48;
    private double _naudioEnvelopeAttackMs = 30;
    private double _naudioEnvelopeDecayMs = 80;
    private double _naudioEnvelopeSustainLevel = 0.75;
    private double _naudioEnvelopeReleaseMs = 200;
    private double _naudioEnvelopeMix = 1;
    private bool _naudioDmoChorusEnabled;
    private double _naudioDmoChorusWetDryMix = 50;
    private double _naudioDmoChorusDepth = 10;
    private double _naudioDmoChorusFeedback = 25;
    private double _naudioDmoChorusFrequencyHz = 1.1;
    private double _naudioDmoChorusWaveForm = 1;
    private double _naudioDmoChorusDelayMs = 16;
    private double _naudioDmoChorusPhase = 3;
    private bool _naudioDmoFlangerEnabled;
    private double _naudioDmoFlangerWetDryMix = 50;
    private double _naudioDmoFlangerDepth = 100;
    private double _naudioDmoFlangerFeedback = -50;
    private double _naudioDmoFlangerFrequencyHz = 0.25;
    private double _naudioDmoFlangerWaveForm = 1;
    private double _naudioDmoFlangerDelayMs = 2;
    private double _naudioDmoFlangerPhase = 2;
    private bool _naudioDmoEchoEnabled;
    private double _naudioDmoEchoWetDryMix = 35;
    private double _naudioDmoEchoFeedback = 30;
    private double _naudioDmoEchoLeftDelayMs = 180;
    private double _naudioDmoEchoRightDelayMs = 220;
    private double _naudioDmoEchoPanDelay;
    private bool _naudioDmoDistortionEnabled;
    private double _naudioDmoDistortionGainDb = -18;
    private double _naudioDmoDistortionEdge = 15;
    private double _naudioDmoDistortionPostEqCenterFrequencyHz = 2400;
    private double _naudioDmoDistortionPostEqBandwidthHz = 2400;
    private double _naudioDmoDistortionPreLowPassCutoffHz = 8000;
    private bool _naudioDmoCompressorEnabled;
    private double _naudioDmoCompressorGainDb;
    private double _naudioDmoCompressorAttackMs = 10;
    private double _naudioDmoCompressorReleaseMs = 200;
    private double _naudioDmoCompressorThresholdDb = -20;
    private double _naudioDmoCompressorRatio = 3;
    private double _naudioDmoCompressorPreDelayMs = 4;
    private bool _naudioDmoParamEqEnabled;
    private double _naudioDmoParamEqCenterFrequencyHz = 8000;
    private double _naudioDmoParamEqBandwidthHz = 12;
    private double _naudioDmoParamEqGainDb;
    private bool _naudioDmoGargleEnabled;
    private double _naudioDmoGargleRateHz = 20;
    private double _naudioDmoGargleWaveShape = 1;
    private bool _naudioDmoI3DL2ReverbEnabled;
    private double _naudioDmoI3DL2Room = -1000;
    private double _naudioDmoI3DL2RoomHf = -100;
    private double _naudioDmoI3DL2RoomRolloffFactor;
    private double _naudioDmoI3DL2DecayTimeSeconds = 1.49;
    private double _naudioDmoI3DL2DecayHfRatio = 0.83;
    private double _naudioDmoI3DL2Reflections = -2602;
    private double _naudioDmoI3DL2ReflectionsDelaySeconds = 0.007;
    private double _naudioDmoI3DL2Reverb = 200;
    private double _naudioDmoI3DL2ReverbDelaySeconds = 0.011;
    private double _naudioDmoI3DL2Diffusion = 100;
    private double _naudioDmoI3DL2Density = 100;
    private double _naudioDmoI3DL2HfReferenceHz = 5000;
    private double _naudioDmoI3DL2Quality = 2;
    private bool _naudioDmoWavesReverbEnabled;
    private double _naudioDmoWavesReverbInGainDb;
    private double _naudioDmoWavesReverbMixDb;
    private double _naudioDmoWavesReverbTimeMs = 1000;
    private double _naudioDmoWavesReverbHighFreqRtRatio = 0.001;

    public double InputTrimDb
    {
        get => _inputTrimDb;
        set => SetField(ref _inputTrimDb, value);
    }

    public bool HighPassEnabled
    {
        get => _highPassEnabled;
        set => SetField(ref _highPassEnabled, value);
    }

    public double HighPassFrequencyHz
    {
        get => _highPassFrequencyHz;
        set => SetField(ref _highPassFrequencyHz, value);
    }

    public bool LowPassEnabled
    {
        get => _lowPassEnabled;
        set => SetField(ref _lowPassEnabled, value);
    }

    public double LowPassFrequencyHz
    {
        get => _lowPassFrequencyHz;
        set => SetField(ref _lowPassFrequencyHz, value);
    }

    public bool HumRemovalEnabled
    {
        get => _humRemovalEnabled;
        set => SetField(ref _humRemovalEnabled, value);
    }

    public double HumRemovalFrequencyHz
    {
        get => _humRemovalFrequencyHz;
        set => SetField(ref _humRemovalFrequencyHz, value);
    }

    public bool NotchFilterEnabled
    {
        get => _notchFilterEnabled;
        set => SetField(ref _notchFilterEnabled, value);
    }

    public double NotchFilterFrequencyHz
    {
        get => _notchFilterFrequencyHz;
        set => SetField(ref _notchFilterFrequencyHz, value);
    }

    public double NotchFilterDepthDb
    {
        get => _notchFilterDepthDb;
        set => SetField(ref _notchFilterDepthDb, value);
    }

    public double NotchFilterQ
    {
        get => _notchFilterQ;
        set => SetField(ref _notchFilterQ, value);
    }

    public bool ParametricEqEnabled
    {
        get => _parametricEqEnabled;
        set => SetField(ref _parametricEqEnabled, value);
    }

    public double ParametricEqFrequencyHz
    {
        get => _parametricEqFrequencyHz;
        set => SetField(ref _parametricEqFrequencyHz, value);
    }

    public double ParametricEqGainDb
    {
        get => _parametricEqGainDb;
        set => SetField(ref _parametricEqGainDb, value);
    }

    public double ParametricEqQ
    {
        get => _parametricEqQ;
        set => SetField(ref _parametricEqQ, value);
    }

    public bool ShelfEqEnabled
    {
        get => _shelfEqEnabled;
        set => SetField(ref _shelfEqEnabled, value);
    }

    public double LowShelfFrequencyHz
    {
        get => _lowShelfFrequencyHz;
        set => SetField(ref _lowShelfFrequencyHz, value);
    }

    public double LowShelfGainDb
    {
        get => _lowShelfGainDb;
        set => SetField(ref _lowShelfGainDb, value);
    }

    public double HighShelfFrequencyHz
    {
        get => _highShelfFrequencyHz;
        set => SetField(ref _highShelfFrequencyHz, value);
    }

    public double HighShelfGainDb
    {
        get => _highShelfGainDb;
        set => SetField(ref _highShelfGainDb, value);
    }

    public bool NoiseGateEnabled
    {
        get => _noiseGateEnabled;
        set => SetField(ref _noiseGateEnabled, value);
    }

    public double NoiseGateThresholdDb
    {
        get => _noiseGateThresholdDb;
        set => SetField(ref _noiseGateThresholdDb, value);
    }

    public double NoiseGateAttackMs
    {
        get => _noiseGateAttackMs;
        set => SetField(ref _noiseGateAttackMs, value);
    }

    public double NoiseGateHoldMs
    {
        get => _noiseGateHoldMs;
        set => SetField(ref _noiseGateHoldMs, value);
    }

    public double NoiseGateReleaseMs
    {
        get => _noiseGateReleaseMs;
        set => SetField(ref _noiseGateReleaseMs, value);
    }

    public double NoiseGateRangeDb
    {
        get => _noiseGateRangeDb;
        set => SetField(ref _noiseGateRangeDb, value);
    }

    public bool ExpanderEnabled
    {
        get => _expanderEnabled;
        set => SetField(ref _expanderEnabled, value);
    }

    public double ExpanderThresholdDb
    {
        get => _expanderThresholdDb;
        set => SetField(ref _expanderThresholdDb, value);
    }

    public double ExpanderRatio
    {
        get => _expanderRatio;
        set => SetField(ref _expanderRatio, value);
    }

    public double ExpanderRangeDb
    {
        get => _expanderRangeDb;
        set => SetField(ref _expanderRangeDb, value);
    }

    public double ExpanderAttackMs
    {
        get => _expanderAttackMs;
        set => SetField(ref _expanderAttackMs, value);
    }

    public double ExpanderHoldMs
    {
        get => _expanderHoldMs;
        set => SetField(ref _expanderHoldMs, value);
    }

    public double ExpanderReleaseMs
    {
        get => _expanderReleaseMs;
        set => SetField(ref _expanderReleaseMs, value);
    }

    public bool NoiseSuppressionEnabled
    {
        get => _noiseSuppressionEnabled;
        set => SetField(ref _noiseSuppressionEnabled, value);
    }

    public double NoiseSuppressionAmountDb
    {
        get => _noiseSuppressionAmountDb;
        set => SetField(ref _noiseSuppressionAmountDb, value);
    }

    public double NoiseSuppressionSensitivity
    {
        get => _noiseSuppressionSensitivity;
        set => SetField(ref _noiseSuppressionSensitivity, value);
    }

    public bool EchoReducerEnabled
    {
        get => _echoReducerEnabled;
        set => SetField(ref _echoReducerEnabled, value);
    }

    public double EchoReducerAmountDb
    {
        get => _echoReducerAmountDb;
        set => SetField(ref _echoReducerAmountDb, value);
    }

    public double EchoReducerSensitivity
    {
        get => _echoReducerSensitivity;
        set => SetField(ref _echoReducerSensitivity, value);
    }

    public bool CompressorEnabled
    {
        get => _compressorEnabled;
        set => SetField(ref _compressorEnabled, value);
    }

    public double CompressorThresholdDb
    {
        get => _compressorThresholdDb;
        set => SetField(ref _compressorThresholdDb, value);
    }

    public double CompressorRatio
    {
        get => _compressorRatio;
        set => SetField(ref _compressorRatio, value);
    }

    public double CompressorAttackMs
    {
        get => _compressorAttackMs;
        set => SetField(ref _compressorAttackMs, value);
    }

    public double CompressorReleaseMs
    {
        get => _compressorReleaseMs;
        set => SetField(ref _compressorReleaseMs, value);
    }

    public double CompressorKneeDb
    {
        get => _compressorKneeDb;
        set => SetField(ref _compressorKneeDb, value);
    }

    public bool DeEsserEnabled
    {
        get => _deEsserEnabled;
        set => SetField(ref _deEsserEnabled, value);
    }

    public double DeEsserAmountDb
    {
        get => _deEsserAmountDb;
        set => SetField(ref _deEsserAmountDb, value);
    }

    public double DeEsserFrequencyHz
    {
        get => _deEsserFrequencyHz;
        set => SetField(ref _deEsserFrequencyHz, value);
    }

    public double DeEsserThresholdDb
    {
        get => _deEsserThresholdDb;
        set => SetField(ref _deEsserThresholdDb, value);
    }

    public double DeEsserRangeDb
    {
        get => _deEsserRangeDb;
        set => SetField(ref _deEsserRangeDb, value);
    }

    public bool BreathReducerEnabled
    {
        get => _breathReducerEnabled;
        set => SetField(ref _breathReducerEnabled, value);
    }

    public double BreathReducerAmountDb
    {
        get => _breathReducerAmountDb;
        set => SetField(ref _breathReducerAmountDb, value);
    }

    public double BreathReducerSensitivity
    {
        get => _breathReducerSensitivity;
        set => SetField(ref _breathReducerSensitivity, value);
    }

    public bool PresenceEnhancerEnabled
    {
        get => _presenceEnhancerEnabled;
        set => SetField(ref _presenceEnhancerEnabled, value);
    }

    public double PresenceEnhancerAmountDb
    {
        get => _presenceEnhancerAmountDb;
        set => SetField(ref _presenceEnhancerAmountDb, value);
    }

    public double PresenceEnhancerFrequencyHz
    {
        get => _presenceEnhancerFrequencyHz;
        set => SetField(ref _presenceEnhancerFrequencyHz, value);
    }

    public double PresenceEnhancerWidthHz
    {
        get => _presenceEnhancerWidthHz;
        set => SetField(ref _presenceEnhancerWidthHz, value);
    }

    public bool SaturationEnabled
    {
        get => _saturationEnabled;
        set => SetField(ref _saturationEnabled, value);
    }

    public double SaturationAmount
    {
        get => _saturationAmount;
        set => SetField(ref _saturationAmount, value);
    }

    public bool DePopperEnabled
    {
        get => _dePopperEnabled;
        set => SetField(ref _dePopperEnabled, value);
    }

    public double DePopperAmountDb
    {
        get => _dePopperAmountDb;
        set => SetField(ref _dePopperAmountDb, value);
    }

    public double DePopperFrequencyHz
    {
        get => _dePopperFrequencyHz;
        set => SetField(ref _dePopperFrequencyHz, value);
    }

    public double DePopperThresholdDb
    {
        get => _dePopperThresholdDb;
        set => SetField(ref _dePopperThresholdDb, value);
    }

    public double MakeupGainDb
    {
        get => _makeupGainDb;
        set => SetField(ref _makeupGainDb, value);
    }

    public bool LimiterEnabled
    {
        get => _limiterEnabled;
        set => SetField(ref _limiterEnabled, value);
    }

    public double LimiterCeilingDb
    {
        get => _limiterCeilingDb;
        set => SetField(ref _limiterCeilingDb, value);
    }

    public bool LimiterSoftClipEnabled
    {
        get => _limiterSoftClipEnabled;
        set => SetField(ref _limiterSoftClipEnabled, value);
    }

    public double LimiterSoftClipDriveDb
    {
        get => _limiterSoftClipDriveDb;
        set => SetField(ref _limiterSoftClipDriveDb, value);
    }

    public bool LimiterLookaheadEnabled
    {
        get => _limiterLookaheadEnabled;
        set => SetField(ref _limiterLookaheadEnabled, value);
    }

    public double LimiterLookaheadMs
    {
        get => _limiterLookaheadMs;
        set => SetField(ref _limiterLookaheadMs, value);
    }

    public double LimiterReleaseMs
    {
        get => _limiterReleaseMs;
        set => SetField(ref _limiterReleaseMs, value);
    }

    public bool NAudioLowPassEnabled
    {
        get => _naudioLowPassEnabled;
        set => SetField(ref _naudioLowPassEnabled, value);
    }

    public double NAudioLowPassFrequencyHz
    {
        get => _naudioLowPassFrequencyHz;
        set => SetField(ref _naudioLowPassFrequencyHz, value);
    }

    public double NAudioLowPassQ
    {
        get => _naudioLowPassQ;
        set => SetField(ref _naudioLowPassQ, value);
    }

    public bool NAudioHighPassEnabled
    {
        get => _naudioHighPassEnabled;
        set => SetField(ref _naudioHighPassEnabled, value);
    }

    public double NAudioHighPassFrequencyHz
    {
        get => _naudioHighPassFrequencyHz;
        set => SetField(ref _naudioHighPassFrequencyHz, value);
    }

    public double NAudioHighPassQ
    {
        get => _naudioHighPassQ;
        set => SetField(ref _naudioHighPassQ, value);
    }

    public bool NAudioBandPassPeakEnabled
    {
        get => _naudioBandPassPeakEnabled;
        set => SetField(ref _naudioBandPassPeakEnabled, value);
    }

    public double NAudioBandPassPeakFrequencyHz
    {
        get => _naudioBandPassPeakFrequencyHz;
        set => SetField(ref _naudioBandPassPeakFrequencyHz, value);
    }

    public double NAudioBandPassPeakQ
    {
        get => _naudioBandPassPeakQ;
        set => SetField(ref _naudioBandPassPeakQ, value);
    }

    public bool NAudioBandPassSkirtEnabled
    {
        get => _naudioBandPassSkirtEnabled;
        set => SetField(ref _naudioBandPassSkirtEnabled, value);
    }

    public double NAudioBandPassSkirtFrequencyHz
    {
        get => _naudioBandPassSkirtFrequencyHz;
        set => SetField(ref _naudioBandPassSkirtFrequencyHz, value);
    }

    public double NAudioBandPassSkirtQ
    {
        get => _naudioBandPassSkirtQ;
        set => SetField(ref _naudioBandPassSkirtQ, value);
    }

    public bool NAudioNotchEnabled
    {
        get => _naudioNotchEnabled;
        set => SetField(ref _naudioNotchEnabled, value);
    }

    public double NAudioNotchFrequencyHz
    {
        get => _naudioNotchFrequencyHz;
        set => SetField(ref _naudioNotchFrequencyHz, value);
    }

    public double NAudioNotchQ
    {
        get => _naudioNotchQ;
        set => SetField(ref _naudioNotchQ, value);
    }

    public bool NAudioAllPassEnabled
    {
        get => _naudioAllPassEnabled;
        set => SetField(ref _naudioAllPassEnabled, value);
    }

    public double NAudioAllPassFrequencyHz
    {
        get => _naudioAllPassFrequencyHz;
        set => SetField(ref _naudioAllPassFrequencyHz, value);
    }

    public double NAudioAllPassQ
    {
        get => _naudioAllPassQ;
        set => SetField(ref _naudioAllPassQ, value);
    }

    public bool NAudioPeakingEqEnabled
    {
        get => _naudioPeakingEqEnabled;
        set => SetField(ref _naudioPeakingEqEnabled, value);
    }

    public double NAudioPeakingEqFrequencyHz
    {
        get => _naudioPeakingEqFrequencyHz;
        set => SetField(ref _naudioPeakingEqFrequencyHz, value);
    }

    public double NAudioPeakingEqQ
    {
        get => _naudioPeakingEqQ;
        set => SetField(ref _naudioPeakingEqQ, value);
    }

    public double NAudioPeakingEqGainDb
    {
        get => _naudioPeakingEqGainDb;
        set => SetField(ref _naudioPeakingEqGainDb, value);
    }

    public bool NAudioLowShelfEnabled
    {
        get => _naudioLowShelfEnabled;
        set => SetField(ref _naudioLowShelfEnabled, value);
    }

    public double NAudioLowShelfFrequencyHz
    {
        get => _naudioLowShelfFrequencyHz;
        set => SetField(ref _naudioLowShelfFrequencyHz, value);
    }

    public double NAudioLowShelfSlope
    {
        get => _naudioLowShelfSlope;
        set => SetField(ref _naudioLowShelfSlope, value);
    }

    public double NAudioLowShelfGainDb
    {
        get => _naudioLowShelfGainDb;
        set => SetField(ref _naudioLowShelfGainDb, value);
    }

    public bool NAudioHighShelfEnabled
    {
        get => _naudioHighShelfEnabled;
        set => SetField(ref _naudioHighShelfEnabled, value);
    }

    public double NAudioHighShelfFrequencyHz
    {
        get => _naudioHighShelfFrequencyHz;
        set => SetField(ref _naudioHighShelfFrequencyHz, value);
    }

    public double NAudioHighShelfSlope
    {
        get => _naudioHighShelfSlope;
        set => SetField(ref _naudioHighShelfSlope, value);
    }

    public double NAudioHighShelfGainDb
    {
        get => _naudioHighShelfGainDb;
        set => SetField(ref _naudioHighShelfGainDb, value);
    }

    public bool NAudioPitchShiftEnabled
    {
        get => _naudioPitchShiftEnabled;
        set => SetField(ref _naudioPitchShiftEnabled, value);
    }

    public double NAudioPitchShiftSemitones
    {
        get => _naudioPitchShiftSemitones;
        set => SetField(ref _naudioPitchShiftSemitones, value);
    }

    public double NAudioPitchShiftFftSize
    {
        get => _naudioPitchShiftFftSize;
        set => SetField(ref _naudioPitchShiftFftSize, value);
    }

    public double NAudioPitchShiftOversampling
    {
        get => _naudioPitchShiftOversampling;
        set => SetField(ref _naudioPitchShiftOversampling, value);
    }

    public double NAudioPitchShiftMix
    {
        get => _naudioPitchShiftMix;
        set => SetField(ref _naudioPitchShiftMix, value);
    }

    public bool NAudioConvolutionEnabled
    {
        get => _naudioConvolutionEnabled;
        set => SetField(ref _naudioConvolutionEnabled, value);
    }

    public double NAudioConvolutionLengthMs
    {
        get => _naudioConvolutionLengthMs;
        set => SetField(ref _naudioConvolutionLengthMs, value);
    }

    public double NAudioConvolutionPreDelayMs
    {
        get => _naudioConvolutionPreDelayMs;
        set => SetField(ref _naudioConvolutionPreDelayMs, value);
    }

    public double NAudioConvolutionDecay
    {
        get => _naudioConvolutionDecay;
        set => SetField(ref _naudioConvolutionDecay, value);
    }

    public double NAudioConvolutionMix
    {
        get => _naudioConvolutionMix;
        set => SetField(ref _naudioConvolutionMix, value);
    }

    public bool NAudioEnvelopeEnabled
    {
        get => _naudioEnvelopeEnabled;
        set => SetField(ref _naudioEnvelopeEnabled, value);
    }

    public double NAudioEnvelopeTriggerThresholdDb
    {
        get => _naudioEnvelopeTriggerThresholdDb;
        set => SetField(ref _naudioEnvelopeTriggerThresholdDb, value);
    }

    public double NAudioEnvelopeAttackMs
    {
        get => _naudioEnvelopeAttackMs;
        set => SetField(ref _naudioEnvelopeAttackMs, value);
    }

    public double NAudioEnvelopeDecayMs
    {
        get => _naudioEnvelopeDecayMs;
        set => SetField(ref _naudioEnvelopeDecayMs, value);
    }

    public double NAudioEnvelopeSustainLevel
    {
        get => _naudioEnvelopeSustainLevel;
        set => SetField(ref _naudioEnvelopeSustainLevel, value);
    }

    public double NAudioEnvelopeReleaseMs
    {
        get => _naudioEnvelopeReleaseMs;
        set => SetField(ref _naudioEnvelopeReleaseMs, value);
    }

    public double NAudioEnvelopeMix
    {
        get => _naudioEnvelopeMix;
        set => SetField(ref _naudioEnvelopeMix, value);
    }

    public bool NAudioDmoChorusEnabled { get => _naudioDmoChorusEnabled; set => SetField(ref _naudioDmoChorusEnabled, value); }

    public double NAudioDmoChorusWetDryMix { get => _naudioDmoChorusWetDryMix; set => SetField(ref _naudioDmoChorusWetDryMix, value); }

    public double NAudioDmoChorusDepth { get => _naudioDmoChorusDepth; set => SetField(ref _naudioDmoChorusDepth, value); }

    public double NAudioDmoChorusFeedback { get => _naudioDmoChorusFeedback; set => SetField(ref _naudioDmoChorusFeedback, value); }

    public double NAudioDmoChorusFrequencyHz { get => _naudioDmoChorusFrequencyHz; set => SetField(ref _naudioDmoChorusFrequencyHz, value); }

    public double NAudioDmoChorusWaveForm { get => _naudioDmoChorusWaveForm; set => SetField(ref _naudioDmoChorusWaveForm, value); }

    public double NAudioDmoChorusDelayMs { get => _naudioDmoChorusDelayMs; set => SetField(ref _naudioDmoChorusDelayMs, value); }

    public double NAudioDmoChorusPhase { get => _naudioDmoChorusPhase; set => SetField(ref _naudioDmoChorusPhase, value); }

    public bool NAudioDmoFlangerEnabled { get => _naudioDmoFlangerEnabled; set => SetField(ref _naudioDmoFlangerEnabled, value); }

    public double NAudioDmoFlangerWetDryMix { get => _naudioDmoFlangerWetDryMix; set => SetField(ref _naudioDmoFlangerWetDryMix, value); }

    public double NAudioDmoFlangerDepth { get => _naudioDmoFlangerDepth; set => SetField(ref _naudioDmoFlangerDepth, value); }

    public double NAudioDmoFlangerFeedback { get => _naudioDmoFlangerFeedback; set => SetField(ref _naudioDmoFlangerFeedback, value); }

    public double NAudioDmoFlangerFrequencyHz { get => _naudioDmoFlangerFrequencyHz; set => SetField(ref _naudioDmoFlangerFrequencyHz, value); }

    public double NAudioDmoFlangerWaveForm { get => _naudioDmoFlangerWaveForm; set => SetField(ref _naudioDmoFlangerWaveForm, value); }

    public double NAudioDmoFlangerDelayMs { get => _naudioDmoFlangerDelayMs; set => SetField(ref _naudioDmoFlangerDelayMs, value); }

    public double NAudioDmoFlangerPhase { get => _naudioDmoFlangerPhase; set => SetField(ref _naudioDmoFlangerPhase, value); }

    public bool NAudioDmoEchoEnabled { get => _naudioDmoEchoEnabled; set => SetField(ref _naudioDmoEchoEnabled, value); }

    public double NAudioDmoEchoWetDryMix { get => _naudioDmoEchoWetDryMix; set => SetField(ref _naudioDmoEchoWetDryMix, value); }

    public double NAudioDmoEchoFeedback { get => _naudioDmoEchoFeedback; set => SetField(ref _naudioDmoEchoFeedback, value); }

    public double NAudioDmoEchoLeftDelayMs { get => _naudioDmoEchoLeftDelayMs; set => SetField(ref _naudioDmoEchoLeftDelayMs, value); }

    public double NAudioDmoEchoRightDelayMs { get => _naudioDmoEchoRightDelayMs; set => SetField(ref _naudioDmoEchoRightDelayMs, value); }

    public double NAudioDmoEchoPanDelay { get => _naudioDmoEchoPanDelay; set => SetField(ref _naudioDmoEchoPanDelay, value); }

    public bool NAudioDmoDistortionEnabled { get => _naudioDmoDistortionEnabled; set => SetField(ref _naudioDmoDistortionEnabled, value); }

    public double NAudioDmoDistortionGainDb { get => _naudioDmoDistortionGainDb; set => SetField(ref _naudioDmoDistortionGainDb, value); }

    public double NAudioDmoDistortionEdge { get => _naudioDmoDistortionEdge; set => SetField(ref _naudioDmoDistortionEdge, value); }

    public double NAudioDmoDistortionPostEqCenterFrequencyHz { get => _naudioDmoDistortionPostEqCenterFrequencyHz; set => SetField(ref _naudioDmoDistortionPostEqCenterFrequencyHz, value); }

    public double NAudioDmoDistortionPostEqBandwidthHz { get => _naudioDmoDistortionPostEqBandwidthHz; set => SetField(ref _naudioDmoDistortionPostEqBandwidthHz, value); }

    public double NAudioDmoDistortionPreLowPassCutoffHz { get => _naudioDmoDistortionPreLowPassCutoffHz; set => SetField(ref _naudioDmoDistortionPreLowPassCutoffHz, value); }

    public bool NAudioDmoCompressorEnabled { get => _naudioDmoCompressorEnabled; set => SetField(ref _naudioDmoCompressorEnabled, value); }

    public double NAudioDmoCompressorGainDb { get => _naudioDmoCompressorGainDb; set => SetField(ref _naudioDmoCompressorGainDb, value); }

    public double NAudioDmoCompressorAttackMs { get => _naudioDmoCompressorAttackMs; set => SetField(ref _naudioDmoCompressorAttackMs, value); }

    public double NAudioDmoCompressorReleaseMs { get => _naudioDmoCompressorReleaseMs; set => SetField(ref _naudioDmoCompressorReleaseMs, value); }

    public double NAudioDmoCompressorThresholdDb { get => _naudioDmoCompressorThresholdDb; set => SetField(ref _naudioDmoCompressorThresholdDb, value); }

    public double NAudioDmoCompressorRatio { get => _naudioDmoCompressorRatio; set => SetField(ref _naudioDmoCompressorRatio, value); }

    public double NAudioDmoCompressorPreDelayMs { get => _naudioDmoCompressorPreDelayMs; set => SetField(ref _naudioDmoCompressorPreDelayMs, value); }

    public bool NAudioDmoParamEqEnabled { get => _naudioDmoParamEqEnabled; set => SetField(ref _naudioDmoParamEqEnabled, value); }

    public double NAudioDmoParamEqCenterFrequencyHz { get => _naudioDmoParamEqCenterFrequencyHz; set => SetField(ref _naudioDmoParamEqCenterFrequencyHz, value); }

    public double NAudioDmoParamEqBandwidthHz { get => _naudioDmoParamEqBandwidthHz; set => SetField(ref _naudioDmoParamEqBandwidthHz, value); }

    public double NAudioDmoParamEqGainDb { get => _naudioDmoParamEqGainDb; set => SetField(ref _naudioDmoParamEqGainDb, value); }

    public bool NAudioDmoGargleEnabled { get => _naudioDmoGargleEnabled; set => SetField(ref _naudioDmoGargleEnabled, value); }

    public double NAudioDmoGargleRateHz { get => _naudioDmoGargleRateHz; set => SetField(ref _naudioDmoGargleRateHz, value); }

    public double NAudioDmoGargleWaveShape { get => _naudioDmoGargleWaveShape; set => SetField(ref _naudioDmoGargleWaveShape, value); }

    public bool NAudioDmoI3DL2ReverbEnabled { get => _naudioDmoI3DL2ReverbEnabled; set => SetField(ref _naudioDmoI3DL2ReverbEnabled, value); }

    public double NAudioDmoI3DL2Room { get => _naudioDmoI3DL2Room; set => SetField(ref _naudioDmoI3DL2Room, value); }

    public double NAudioDmoI3DL2RoomHf { get => _naudioDmoI3DL2RoomHf; set => SetField(ref _naudioDmoI3DL2RoomHf, value); }

    public double NAudioDmoI3DL2RoomRolloffFactor { get => _naudioDmoI3DL2RoomRolloffFactor; set => SetField(ref _naudioDmoI3DL2RoomRolloffFactor, value); }

    public double NAudioDmoI3DL2DecayTimeSeconds { get => _naudioDmoI3DL2DecayTimeSeconds; set => SetField(ref _naudioDmoI3DL2DecayTimeSeconds, value); }

    public double NAudioDmoI3DL2DecayHfRatio { get => _naudioDmoI3DL2DecayHfRatio; set => SetField(ref _naudioDmoI3DL2DecayHfRatio, value); }

    public double NAudioDmoI3DL2Reflections { get => _naudioDmoI3DL2Reflections; set => SetField(ref _naudioDmoI3DL2Reflections, value); }

    public double NAudioDmoI3DL2ReflectionsDelaySeconds { get => _naudioDmoI3DL2ReflectionsDelaySeconds; set => SetField(ref _naudioDmoI3DL2ReflectionsDelaySeconds, value); }

    public double NAudioDmoI3DL2Reverb { get => _naudioDmoI3DL2Reverb; set => SetField(ref _naudioDmoI3DL2Reverb, value); }

    public double NAudioDmoI3DL2ReverbDelaySeconds { get => _naudioDmoI3DL2ReverbDelaySeconds; set => SetField(ref _naudioDmoI3DL2ReverbDelaySeconds, value); }

    public double NAudioDmoI3DL2Diffusion { get => _naudioDmoI3DL2Diffusion; set => SetField(ref _naudioDmoI3DL2Diffusion, value); }

    public double NAudioDmoI3DL2Density { get => _naudioDmoI3DL2Density; set => SetField(ref _naudioDmoI3DL2Density, value); }

    public double NAudioDmoI3DL2HfReferenceHz { get => _naudioDmoI3DL2HfReferenceHz; set => SetField(ref _naudioDmoI3DL2HfReferenceHz, value); }

    public double NAudioDmoI3DL2Quality { get => _naudioDmoI3DL2Quality; set => SetField(ref _naudioDmoI3DL2Quality, value); }

    public bool NAudioDmoWavesReverbEnabled { get => _naudioDmoWavesReverbEnabled; set => SetField(ref _naudioDmoWavesReverbEnabled, value); }

    public double NAudioDmoWavesReverbInGainDb { get => _naudioDmoWavesReverbInGainDb; set => SetField(ref _naudioDmoWavesReverbInGainDb, value); }

    public double NAudioDmoWavesReverbMixDb { get => _naudioDmoWavesReverbMixDb; set => SetField(ref _naudioDmoWavesReverbMixDb, value); }

    public double NAudioDmoWavesReverbTimeMs { get => _naudioDmoWavesReverbTimeMs; set => SetField(ref _naudioDmoWavesReverbTimeMs, value); }

    public double NAudioDmoWavesReverbHighFreqRtRatio { get => _naudioDmoWavesReverbHighFreqRtRatio; set => SetField(ref _naudioDmoWavesReverbHighFreqRtRatio, value); }

    public int EqualizerRevision => System.Threading.Volatile.Read(ref _equalizerRevision);

    public int SettingsRevision => System.Threading.Volatile.Read(ref _settingsRevision);

    public void SetEqualizerGains(IReadOnlyList<double> gainsDb)
    {
        var changed = false;
        lock (_equalizerLock)
        {
            for (var i = 0; i < _equalizerGainsDb.Length; i++)
            {
                var gainDb = i < gainsDb.Count
                    ? Math.Clamp(gainsDb[i], -12d, 12d)
                    : 0d;
                if (Math.Abs(_equalizerGainsDb[i] - gainDb) < 0.000001d)
                {
                    continue;
                }

                _equalizerGainsDb[i] = gainDb;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        System.Threading.Interlocked.Increment(ref _equalizerRevision);
        System.Threading.Interlocked.Increment(ref _settingsRevision);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SetEqualizerGains)));
    }

    public void CopyEqualizerGainsTo(Span<double> destination)
    {
        lock (_equalizerLock)
        {
            _equalizerGainsDb.AsSpan(0, Math.Min(_equalizerGainsDb.Length, destination.Length)).CopyTo(destination);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        System.Threading.Interlocked.Increment(ref _settingsRevision);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


