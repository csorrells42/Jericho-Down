namespace VoiceWorkbench.Wifi;

public sealed class WifiNetwork
{
    public string Ssid { get; init; } = "";

    public string Bssid { get; init; } = "";

    public int SignalPercent { get; init; }

    public string Band { get; init; } = "";

    public int Channel { get; init; }

    public string RadioType { get; init; } = "";

    public string Authentication { get; init; } = "";

    public string Encryption { get; init; } = "";

    public DateTime LastSeen { get; init; } = DateTime.Now;

    public bool IsCached { get; init; }

    public string SignalLabel => $"{SignalPercent}%";

    public string ChannelLabel => Channel > 0 ? Channel.ToString() : "";

    public string SeenLabel => IsCached ? $"cached {LastSeen:h:mm:ss tt}" : "live";
}
