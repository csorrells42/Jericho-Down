using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PodcastWorkbench.Audio;

namespace PodcastWorkbench;

public partial class SpectrumWindow : Window
{
    private readonly MicrophoneSpectrumService _spectrumService = new();
    private SpectrumFrame? _latestFrame;
    private readonly List<Line> _gridLines = [];
    private readonly Polyline _liveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 3,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _averageTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(215, 178, 32)),
        StrokeThickness = 2,
        Opacity = 0.9,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly double[] _averageMagnitudes = new double[96];
    private double[] _renderedMagnitudes = [];
    private double[] _renderedAverageMagnitudes = [];
    private double _visualCeiling = 0.25d;
    private int _silentFrameCount;
    private AudioInputDevice? _selectedDevice;

    public SpectrumWindow()
    {
        InitializeComponent();
        SpectrumCanvas.Children.Add(_averageTrace);
        SpectrumCanvas.Children.Add(_liveTrace);
        _spectrumService.SpectrumAvailable += SpectrumAvailable;
        CompositionTarget.Rendering += CompositionTargetRendering;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        var devices = MicrophoneSpectrumService.GetInputDevices();
        MicrophoneComboBox.ItemsSource = devices;
        if (devices.Count > 0)
        {
            MicrophoneComboBox.SelectedIndex = 0;
        }
        else
        {
            StatusText.Text = "No microphones found";
        }
    }

    private void WindowActivated(object? sender, EventArgs e)
    {
        StartListening();
    }

    private void WindowDeactivated(object? sender, EventArgs e)
    {
        if (_spectrumService.IsRunning)
        {
            StatusText.Text = "Listening";
        }
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        CompositionTarget.Rendering -= CompositionTargetRendering;
        _spectrumService.SpectrumAvailable -= SpectrumAvailable;
        _spectrumService.Dispose();
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MicrophoneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = MicrophoneComboBox.SelectedItem as AudioInputDevice;
        _spectrumService.Stop();
        StartListening();
    }

    private void StartListening()
    {
        if (_selectedDevice is null)
        {
            return;
        }

        try
        {
            _spectrumService.Start(_selectedDevice.DeviceNumber);
            StatusText.Text = "Listening";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Microphone unavailable: {ex.Message}";
        }
    }

    private void SpectrumAvailable(object? sender, SpectrumFrame frame)
    {
        _latestFrame = frame;
    }

    private void CompositionTargetRendering(object? sender, EventArgs e)
    {
        if (_latestFrame is null)
        {
            return;
        }

        RenderSpectrum(_latestFrame);
    }

    private void RenderSpectrum(SpectrumFrame frame)
    {
        var width = Math.Max(1d, SpectrumCanvas.ActualWidth);
        var height = Math.Max(1d, SpectrumCanvas.ActualHeight);
        var topInset = 110d;
        var usableHeight = Math.Max(1d, height - topInset - 28d);
        var graphTop = topInset;
        var graphBottom = graphTop + usableHeight;

        EnsureGraphGrid(width, graphTop, graphBottom);

        var livePoints = new PointCollection();
        var averagePoints = new PointCollection();
        var frameCeiling = Math.Max(0.08d, frame.Magnitudes.Select(ShapeMagnitude).DefaultIfEmpty(0.08d).Max());
        _visualCeiling = frameCeiling > _visualCeiling
            ? Ease(_visualCeiling, frameCeiling, 0.08d)
            : Ease(_visualCeiling, frameCeiling, 0.015d);
        EnsureRenderBuffers(frame.Magnitudes.Length);

        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            var visualMagnitude = NormalizeForDisplay(ShapeMagnitude(frame.Magnitudes[i]));
            _renderedMagnitudes[i] = Ease(_renderedMagnitudes[i], visualMagnitude, 0.105d);
            _renderedAverageMagnitudes[i] = Ease(_renderedAverageMagnitudes[i], _renderedMagnitudes[i], 0.022d);

            var x = frame.Magnitudes.Length == 1
                ? 0d
                : i / (double)(frame.Magnitudes.Length - 1) * width;
            livePoints.Add(new Point(x, graphBottom - usableHeight * _renderedMagnitudes[i]));
            averagePoints.Add(new Point(x, graphBottom - usableHeight * _renderedAverageMagnitudes[i]));
        }

        _liveTrace.Points = SmoothPoints(livePoints);
        _averageTrace.Points = SmoothPoints(averagePoints);
        PeakText.Text = $"Peak {frame.PeakLevel:P0}";
        UpdateSignalStatus(frame.PeakLevel);
    }

    private void EnsureGraphGrid(double width, double graphTop, double graphBottom)
    {
        var neededLines = 17;
        while (_gridLines.Count < neededLines)
        {
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(36, 45, 54)),
                StrokeThickness = 1,
                Opacity = 0.82
            };
            _gridLines.Add(line);
            SpectrumCanvas.Children.Insert(0, line);
        }

        for (var i = 0; i < 9; i++)
        {
            var y = graphTop + (graphBottom - graphTop) * i / 8d;
            _gridLines[i].X1 = 0;
            _gridLines[i].Y1 = y;
            _gridLines[i].X2 = width;
            _gridLines[i].Y2 = y;
        }

        for (var i = 0; i < 8; i++)
        {
            var x = width * i / 7d;
            var line = _gridLines[9 + i];
            line.X1 = x;
            line.Y1 = graphTop;
            line.X2 = x;
            line.Y2 = graphBottom;
        }
    }

    private static double ShapeMagnitude(double magnitude)
    {
        var lifted = Math.Max(0d, magnitude - 0.01d);
        return Math.Clamp(Math.Pow(lifted, 0.9d), 0d, 1d);
    }

    private double NormalizeForDisplay(double magnitude)
    {
        var gain = DisplayGainSlider.Value;
        return Math.Clamp(magnitude / Math.Max(0.08d, _visualCeiling) * 0.62d * gain / 6d, 0d, 0.88d);
    }

    private void EnsureRenderBuffers(int length)
    {
        if (_renderedMagnitudes.Length == length)
        {
            return;
        }

        _renderedMagnitudes = new double[length];
        _renderedAverageMagnitudes = new double[length];
    }

    private static double Ease(double current, double target, double amount)
    {
        return current + (target - current) * amount;
    }

    private static PointCollection SmoothPoints(PointCollection source)
    {
        if (source.Count < 4)
        {
            return source;
        }

        var smoothed = new PointCollection(source.Count * 2);
        smoothed.Add(source[0]);

        for (var i = 1; i < source.Count - 2; i++)
        {
            var p0 = source[i - 1];
            var p1 = source[i];
            var p2 = source[i + 1];
            var p3 = source[i + 2];

            smoothed.Add(p1);
            smoothed.Add(CatmullRom(p0, p1, p2, p3, 0.5d));
        }

        smoothed.Add(source[^2]);
        smoothed.Add(source[^1]);
        return smoothed;
    }

    private static Point CatmullRom(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var x = 0.5d * ((2d * p1.X) + (-p0.X + p2.X) * t + (2d * p0.X - 5d * p1.X + 4d * p2.X - p3.X) * t2 + (-p0.X + 3d * p1.X - 3d * p2.X + p3.X) * t3);
        var y = 0.5d * ((2d * p1.Y) + (-p0.Y + p2.Y) * t + (2d * p0.Y - 5d * p1.Y + 4d * p2.Y - p3.Y) * t2 + (-p0.Y + 3d * p1.Y - 3d * p2.Y + p3.Y) * t3);
        return new Point(x, y);
    }

    private void UpdateSignalStatus(double peakLevel)
    {
        if (peakLevel < 0.001d)
        {
            _silentFrameCount++;
        }
        else
        {
            _silentFrameCount = 0;
        }

        if (_silentFrameCount > 20)
        {
            StatusText.Text = "Listening, but this input is silent. Try another mic/device or raise input gain.";
        }
        else if (_selectedDevice is not null)
        {
            StatusText.Text = "Listening";
        }
    }
}


