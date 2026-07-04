namespace PodcastWorkbench.Video;

public sealed class CameraDevice
{
    public CameraDevice(int deviceNumber, string name, string devicePath, string source = "")
    {
        DeviceNumber = deviceNumber;
        Name = name;
        DevicePath = devicePath;
        Source = source;
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public string DevicePath { get; }

    public string Source { get; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Source)
            ? Name
            : $"{Name} ({Source})";
    }
}

