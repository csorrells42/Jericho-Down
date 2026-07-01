namespace PodcastWorkbench.Audio;

public sealed record AudioOutputDevice(int DeviceNumber, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}

