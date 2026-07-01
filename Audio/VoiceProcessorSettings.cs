using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PodcastWorkbench.Audio;

public sealed class VoiceProcessorSettings : INotifyPropertyChanged
{
    private bool _highPassEnabled = true;
    private double _highPassFrequencyHz = 80;
    private bool _noiseGateEnabled = true;
    private double _noiseGateThresholdDb = -48;
    private bool _noiseSuppressionEnabled;
    private double _noiseSuppressionAmountDb = 6;
    private bool _echoReducerEnabled;
    private double _echoReducerAmountDb = 4;
    private bool _compressorEnabled = true;
    private double _compressorThresholdDb = -18;
    private double _compressorRatio = 3;
    private bool _deEsserEnabled;
    private double _deEsserAmountDb = 3;
    private bool _dePopperEnabled;
    private double _dePopperAmountDb = 6;
    private double _makeupGainDb = 2;
    private bool _limiterEnabled = true;
    private double _limiterCeilingDb = -1;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


