namespace JerichoDown.Video;

public sealed class CameraDevice
{
    public CameraDevice(
        int deviceNumber,
        string name,
        string devicePath,
        string source = "",
        CameraDevice? fallbackDevice = null)
    {
        DeviceNumber = deviceNumber;
        Name = name;
        DevicePath = devicePath;
        Source = source;
        FallbackDevice = fallbackDevice;
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public string DevicePath { get; }

    public string Source { get; }

    public CameraDevice? FallbackDevice { get; }

    public bool HasFallbackDevice => FallbackDevice is not null;

    public CameraDevice WithFallback(CameraDevice fallbackDevice)
    {
        return new CameraDevice(DeviceNumber, Name, DevicePath, Source, fallbackDevice);
    }

    public IEnumerable<CameraDevice> EnumerateSourceDevices()
    {
        yield return this;
        if (FallbackDevice is not null)
        {
            yield return FallbackDevice;
        }
    }

    public override string ToString()
    {
        if (HasFallbackDevice)
        {
            return Name;
        }

        return string.IsNullOrWhiteSpace(Source)
            ? Name
            : $"{Name} ({Source})";
    }
}

