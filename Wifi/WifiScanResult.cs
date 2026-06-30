namespace VoiceWorkbench.Wifi;

public sealed class WifiScanResult
{
    public WifiScanResult(IReadOnlyList<WifiNetwork> networks, string status)
    {
        Networks = networks;
        Status = status;
    }

    public IReadOnlyList<WifiNetwork> Networks { get; }

    public string Status { get; }
}
