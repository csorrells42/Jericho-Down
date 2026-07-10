using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace JerichoDown.Audio;

public sealed class AudioDeviceNotificationWatcher : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly NotificationClient _client;
    private bool _disposed;

    public AudioDeviceNotificationWatcher()
    {
        _enumerator = new MMDeviceEnumerator();
        _client = new NotificationClient(OnDevicesChanged);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    public event EventHandler<AudioDeviceChangedEventArgs>? DevicesChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_client);
        }
        catch
        {
        }

        _enumerator.Dispose();
    }

    private void OnDevicesChanged(AudioDeviceChangedEventArgs args)
    {
        if (!_disposed)
        {
            DevicesChanged?.Invoke(this, args);
        }
    }

    private sealed class NotificationClient(Action<AudioDeviceChangedEventArgs> notify) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            notify(new AudioDeviceChangedEventArgs(AudioDeviceChangeKind.StateChanged, deviceId, null, null));
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            notify(new AudioDeviceChangedEventArgs(AudioDeviceChangeKind.Added, pwstrDeviceId, null, null));
        }

        public void OnDeviceRemoved(string deviceId)
        {
            notify(new AudioDeviceChangedEventArgs(AudioDeviceChangeKind.Removed, deviceId, null, null));
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            notify(new AudioDeviceChangedEventArgs(AudioDeviceChangeKind.DefaultChanged, defaultDeviceId, flow, role));
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            notify(new AudioDeviceChangedEventArgs(AudioDeviceChangeKind.PropertyChanged, pwstrDeviceId, null, null));
        }
    }
}

public sealed record AudioDeviceChangedEventArgs(
    AudioDeviceChangeKind Kind,
    string? DeviceId,
    DataFlow? Flow,
    Role? Role);

public enum AudioDeviceChangeKind
{
    Added,
    Removed,
    StateChanged,
    DefaultChanged,
    PropertyChanged
}
