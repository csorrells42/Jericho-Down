using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PodcastWorkbench.Audio;

public sealed class EqualizerBand : INotifyPropertyChanged
{
    private double _gainDb;

    public EqualizerBand(string label, double centerFrequencyHz)
    {
        Label = label;
        CenterFrequencyHz = centerFrequencyHz;
    }

    public string Label { get; }

    public double CenterFrequencyHz { get; }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GainDb)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}


