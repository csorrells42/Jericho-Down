using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed class EqualizerBand : INotifyPropertyChanged
{
    private double _gainDb;
    private bool _isEnabled = true;

    public EqualizerBand(string label, double centerFrequencyHz)
    {
        Label = label;
        CenterFrequencyHz = centerFrequencyHz;
    }

    public string Label { get; }

    public double CenterFrequencyHz { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(IsBoost));
            OnPropertyChanged(nameof(IsCut));
        }
    }

    public double GainDb
    {
        get => _gainDb;
        set
        {
            if (Math.Abs(_gainDb - value) < 0.01d)
            {
                return;
            }

            _gainDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(IsBoost));
            OnPropertyChanged(nameof(IsCut));
        }
    }

    public bool IsBoost => IsEnabled && GainDb > 0.05d;

    public bool IsCut => IsEnabled && GainDb < -0.05d;

    public string DisplayValue => IsEnabled ? $"{GainDb:+0;-0;0}" : "OFF";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


