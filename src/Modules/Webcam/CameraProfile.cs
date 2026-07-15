namespace JerichoDown.Modules.Webcam;

public sealed class CameraProfile
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string? CameraName { get; set; }

    public string? CameraSource { get; set; }

    public string? CameraDevicePath { get; set; }

    public bool CameraEnabled { get; set; }

    public string ModeLabel { get; set; } = CameraVideoMode.Auto.Label;

    public int? ModeWidth { get; set; }

    public int? ModeHeight { get; set; }

    public double? ModeFramesPerSecond { get; set; }

    public string? ModeInputFormat { get; set; }

    public bool DenoiseEnabled { get; set; }

    public double DenoiseStrength { get; set; } = 2d;

    public bool ColorPolishEnabled { get; set; }

    public double Exposure { get; set; }

    public double Contrast { get; set; }

    public double Saturation { get; set; }

    public double Warmth { get; set; }
}
