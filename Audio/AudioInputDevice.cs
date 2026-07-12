namespace JerichoDown.Audio;

public sealed class AudioInputDevice
{
    public const int SystemAudioLoopbackDeviceNumber = -1000;
    public const int AsioInputDeviceNumber = -2000;
    public const int ProcessLoopbackDeviceNumberOffset = -1000000;
    public const string SystemAudioLoopbackDeviceName = "Computer audio (loopback)";
    public const string ProcessLoopbackEndpointPrefix = "process-loopback:";

    public AudioInputDevice(
        int deviceNumber,
        string name,
        int maximumInputChannels = 0,
        string? endpointId = null,
        AudioInputBackend backend = AudioInputBackend.Windows)
    {
        DeviceNumber = deviceNumber;
        Name = name;
        MaximumInputChannels = Math.Max(0, maximumInputChannels);
        EndpointId = endpointId;
        Backend = backend;
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public int MaximumInputChannels { get; }

    public string? EndpointId { get; }

    public AudioInputBackend Backend { get; }

    public bool IsSystemAudioLoopback => DeviceNumber == SystemAudioLoopbackDeviceNumber;

    public bool IsProcessLoopback => Backend == AudioInputBackend.ProcessLoopback
        || TryGetProcessLoopbackTargetProcessId(DeviceNumber, EndpointId, out _);

    public bool IsAsio => Backend == AudioInputBackend.Asio;

    public static AudioInputDevice CreateSystemAudioLoopback()
    {
        return new AudioInputDevice(SystemAudioLoopbackDeviceNumber, SystemAudioLoopbackDeviceName, 2);
    }

    public static AudioInputDevice CreateProcessLoopback(int processId, string displayName)
    {
        var targetProcessId = Math.Max(1, processId);
        var safeName = string.IsNullOrWhiteSpace(displayName)
            ? $"PID {targetProcessId}"
            : displayName.Trim();
        return new AudioInputDevice(
            ProcessLoopbackDeviceNumberOffset - targetProcessId,
            $"App audio: {safeName} (pid {targetProcessId})",
            2,
            $"{ProcessLoopbackEndpointPrefix}{targetProcessId}",
            AudioInputBackend.ProcessLoopback);
    }

    public static bool TryGetProcessLoopbackTargetProcessId(
        int deviceNumber,
        string? endpointId,
        out int processId)
    {
        processId = 0;
        if (!string.IsNullOrWhiteSpace(endpointId)
            && endpointId.StartsWith(ProcessLoopbackEndpointPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(endpointId[ProcessLoopbackEndpointPrefix.Length..], out var endpointProcessId)
            && endpointProcessId > 0)
        {
            processId = endpointProcessId;
            return true;
        }

        if (deviceNumber < ProcessLoopbackDeviceNumberOffset)
        {
            var encodedProcessId = ProcessLoopbackDeviceNumberOffset - deviceNumber;
            if (encodedProcessId > 0)
            {
                processId = encodedProcessId;
                return true;
            }
        }

        return false;
    }

    public override string ToString() => Name;
}

public enum AudioInputBackend
{
    Windows,
    Asio,
    ProcessLoopback
}
