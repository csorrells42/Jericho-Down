namespace JerichoDown.Audio;

public sealed class AudioInputDevice
{
    public AudioInputDevice(int deviceNumber, string name, int maximumInputChannels = 0)
    {
        DeviceNumber = deviceNumber;
        Name = name;
        MaximumInputChannels = Math.Max(0, maximumInputChannels);
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public int MaximumInputChannels { get; }

    public override string ToString() => Name;
}


