using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using JerichoDown.Audio;
using Microsoft.Win32;

namespace JerichoDown;

public partial class VerificationView : UserControl
{
    private DspVerificationReport? _report;

    public VerificationView()
    {
        InitializeComponent();
        RunVerification();
    }

    private void RunAgainClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        RunVerification();
    }

    private void SaveReportClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_report is null)
        {
            RunVerification();
        }

        if (_report is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save DSP Verification Report",
            FileName = string.Format(
                CultureInfo.InvariantCulture,
                "jericho-down-dsp-verification-{0:yyyyMMdd-HHmmss}.md",
                _report.GeneratedAt),
            Filter = "Markdown report (*.md)|*.md|Text report (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AtomicFile.WriteAllText(dialog.FileName, DspVerificationReportGenerator.CreateMarkdownReport(_report));
        StatusText.Text = "Saved verification report: " + dialog.FileName;
    }

    private void RunVerification()
    {
        StatusText.Text = "Running DSP verification...";
        _report = DspVerificationReportGenerator.Run();
        VerificationGrid.ItemsSource = _report.Checks;
        ResultText.Text = _report.Passed ? "PASS" : "FAIL";
        ResultText.Foreground = new SolidColorBrush(_report.Passed ? Color.FromRgb(53, 232, 216) : Color.FromRgb(248, 113, 113));
        SummaryText.Text = _report.Summary;
        GeneratedText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Generated {0:yyyy-MM-dd HH:mm:ss zzz}\n{1} {2}\nSample rate: {3:N0} Hz",
            _report.GeneratedAt,
            _report.AssemblyName,
            _report.AssemblyVersion,
            _report.SampleRate);
        StatusText.Text = _report.Passed
            ? "All verification checks passed."
            : "One or more verification checks failed. Save the report for details.";
    }
}
