using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JerichoDown.Audio;

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


