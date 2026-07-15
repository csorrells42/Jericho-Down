namespace JerichoDown.Modules.Audio.Devices;

public sealed record AudioOutputDevice(
    int DeviceNumber,
    string Name,
    string? EndpointId = null,
    AudioOutputBackend Backend = AudioOutputBackend.Windows)
{
    public bool IsAsio => Backend == AudioOutputBackend.Asio;

    public override string ToString()
    {
        return Name;
    }
}

public enum AudioOutputBackend
{
    Windows,
    Asio
}

