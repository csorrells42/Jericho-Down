using NAudio.Wave;

namespace JerichoDown.Audio;

public sealed class AsioOutputPlayer : IWavePlayer
{
    private readonly StaThreadDispatcher _dispatcher;
    private AsioOut? _asio;
    private float _volume = 1f;
    private bool _disposed;

    public AsioOutputPlayer(string driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new ArgumentException("Choose a valid ASIO driver.", nameof(driverName));
        }

        DriverName = driverName.Trim();
        _dispatcher = new StaThreadDispatcher($"Jericho ASIO Output ({DriverName})");
    }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public string DriverName { get; }

    public PlaybackState PlaybackState => _dispatcher.Invoke(() => _asio?.PlaybackState ?? PlaybackState.Stopped);

    public float Volume
    {
        get => _volume;
        set => _volume = value;
    }

    public WaveFormat OutputWaveFormat => _dispatcher.Invoke(() => _asio?.OutputWaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));

    public void Init(IWaveProvider waveProvider)
    {
        ArgumentNullException.ThrowIfNull(waveProvider);
        _dispatcher.Invoke(() =>
        {
            ThrowIfDisposed();
            if (_asio is not null)
            {
                throw new InvalidOperationException("ASIO output is already initialized.");
            }

            var asio = new AsioOut(DriverName);
            asio.PlaybackStopped += OnPlaybackStopped;
            try
            {
                asio.Init(waveProvider);
                _asio = asio;
            }
            catch
            {
                asio.PlaybackStopped -= OnPlaybackStopped;
                asio.Dispose();
                throw;
            }
        });
    }

    public void Play()
    {
        _dispatcher.Invoke(() => _asio?.Play());
    }

    public void Pause()
    {
        _dispatcher.Invoke(() => _asio?.Pause());
    }

    public void Stop()
    {
        _dispatcher.Invoke(() => _asio?.Stop());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _dispatcher.Invoke(() =>
            {
                var asio = _asio;
                _asio = null;
                if (asio is null)
                {
                    return;
                }

                asio.PlaybackStopped -= OnPlaybackStopped;
                try
                {
                    asio.Stop();
                }
                catch
                {
                }

                asio.Dispose();
            });
        }
        finally
        {
            _dispatcher.Dispose();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackStopped?.Invoke(this, e);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
