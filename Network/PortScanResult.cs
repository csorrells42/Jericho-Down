namespace VoiceWorkbench.Network;

public sealed class PortScanResult
{
    public int Port { get; init; }

    public string Protocol { get; init; } = "TCP";

    public string State { get; init; } = "";

    public string Service { get; init; } = "";

    public string Notes { get; init; } = "";
}
