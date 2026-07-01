namespace PodcastWorkbench.Audio;

public sealed class AudioInputDevice
{
    public AudioInputDevice(int deviceNumber, string name)
    {
        DeviceNumber = deviceNumber;
        Name = name;
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public override string ToString() => Name;
}


