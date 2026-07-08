namespace JerichoDown.Audio;

public sealed record AudioOutputDevice(int DeviceNumber, string Name, string? EndpointId = null)
{
    public override string ToString()
    {
        return Name;
    }
}

