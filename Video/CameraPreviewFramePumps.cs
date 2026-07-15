using System.Windows.Threading;
using JerichoDown.Modules.Webcam;

namespace JerichoDown.Video;

internal sealed class TextureNativeStatusPump
{
    private const int WarningFrameReplacementInterval = 50;
    private static readonly long WarningFrameReplacementWindowTicks = TimeSpan.FromSeconds(2).Ticks;

    private readonly Dispatcher _dispatcher;
    private readonly Action<TextureNativeFrameInfo> _processFrame;
    private readonly Action<string>? _warningSink;
    private TextureNativeFrameInfo? _pendingFrame;
    private int _frameUpdateQueued;
    private int _framesReplacedSinceWarning;
    private long _replacementWindowStartTicks;
    private int _warningQueued;

    public TextureNativeStatusPump(
        Dispatcher dispatcher,
        Action<TextureNativeFrameInfo> processFrame,
        Action<string>? warningSink = null)
    {
        _dispatcher = dispatcher;
        _processFrame = processFrame;
        _warningSink = warningSink;
    }

    public void FrameAvailable(TextureNativeFrameInfo frame)
    {
        if (Interlocked.Exchange(ref _pendingFrame, frame) is not null)
        {
            TrackFrameReplacement("DX12 status");
        }

        if (Interlocked.Exchange(ref _frameUpdateQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke((Action)ProcessPendingFrame, DispatcherPriority.Background);
    }

    public void Reset()
    {
        _pendingFrame = null;
        Volatile.Write(ref _frameUpdateQueued, 0);
        Volatile.Write(ref _framesReplacedSinceWarning, 0);
        Volatile.Write(ref _replacementWindowStartTicks, 0);
    }

    private void ProcessPendingFrame()
    {
        var frame = Interlocked.Exchange(ref _pendingFrame, null);
        if (frame is not null)
        {
            _processFrame(frame);
        }

        Volatile.Write(ref _frameUpdateQueued, 0);
        if (Volatile.Read(ref _pendingFrame) is not null
            && Interlocked.Exchange(ref _frameUpdateQueued, 1) == 0)
        {
            _dispatcher.BeginInvoke((Action)ProcessPendingFrame, DispatcherPriority.Background);
        }
    }

    private void TrackFrameReplacement(string pumpName)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (Volatile.Read(ref _replacementWindowStartTicks) == 0)
        {
            Interlocked.CompareExchange(ref _replacementWindowStartTicks, nowTicks, 0);
        }

        var replaced = Interlocked.Increment(ref _framesReplacedSinceWarning);
        if (replaced < WarningFrameReplacementInterval)
        {
            return;
        }

        var startTicks = Volatile.Read(ref _replacementWindowStartTicks);
        var elapsedTicks = Math.Max(0, nowTicks - startTicks);
        Interlocked.Exchange(ref _framesReplacedSinceWarning, 0);
        Volatile.Write(ref _replacementWindowStartTicks, nowTicks);

        if (elapsedTicks <= WarningFrameReplacementWindowTicks)
        {
            var elapsedSeconds = TimeSpan.FromTicks(elapsedTicks).TotalSeconds;
            QueueWarning($"{pumpName} camera pump replaced {WarningFrameReplacementInterval} frames in {elapsedSeconds:0.0}s before the UI caught up.");
        }
    }

    private void QueueWarning(string warning)
    {
        if (_warningSink is null || Interlocked.Exchange(ref _warningQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _warningQueued, 0);
            _warningSink(warning);
        }, DispatcherPriority.Background);
    }
}

internal sealed class CpuPreviewFramePump
{
    private const int WarningFrameReplacementInterval = 50;
    private static readonly long WarningFrameReplacementWindowTicks = TimeSpan.FromSeconds(2).Ticks;

    private readonly Dispatcher _dispatcher;
    private readonly Action<CameraFrame> _processFrame;
    private readonly Action<string>? _warningSink;
    private CameraFrame? _pendingFrame;
    private bool _frameUpdateQueued;
    private int _framesReplacedSinceWarning;
    private long _replacementWindowStartTicks;
    private int _warningQueued;

    public CpuPreviewFramePump(
        Dispatcher dispatcher,
        Action<CameraFrame> processFrame,
        Action<string>? warningSink = null)
    {
        _dispatcher = dispatcher;
        _processFrame = processFrame;
        _warningSink = warningSink;
    }

    public void FrameAvailable(CameraFrame frame)
    {
        _pendingFrame = frame;
        if (_frameUpdateQueued)
        {
            TrackFrameReplacement("CPU preview");
            return;
        }

        _frameUpdateQueued = true;
        _dispatcher.BeginInvoke(() =>
        {
            var latestFrame = _pendingFrame;
            _pendingFrame = null;
            _frameUpdateQueued = false;

            if (latestFrame is not null)
            {
                _processFrame(latestFrame);
            }
        });
    }

    public void Reset()
    {
        _frameUpdateQueued = false;
        _pendingFrame = null;
        Volatile.Write(ref _framesReplacedSinceWarning, 0);
        Volatile.Write(ref _replacementWindowStartTicks, 0);
    }

    private void TrackFrameReplacement(string pumpName)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (Volatile.Read(ref _replacementWindowStartTicks) == 0)
        {
            Interlocked.CompareExchange(ref _replacementWindowStartTicks, nowTicks, 0);
        }

        var replaced = Interlocked.Increment(ref _framesReplacedSinceWarning);
        if (replaced < WarningFrameReplacementInterval)
        {
            return;
        }

        var startTicks = Volatile.Read(ref _replacementWindowStartTicks);
        var elapsedTicks = Math.Max(0, nowTicks - startTicks);
        Interlocked.Exchange(ref _framesReplacedSinceWarning, 0);
        Volatile.Write(ref _replacementWindowStartTicks, nowTicks);

        if (elapsedTicks <= WarningFrameReplacementWindowTicks)
        {
            var elapsedSeconds = TimeSpan.FromTicks(elapsedTicks).TotalSeconds;
            QueueWarning($"{pumpName} camera pump replaced {WarningFrameReplacementInterval} frames in {elapsedSeconds:0.0}s before the UI caught up.");
        }
    }

    private void QueueWarning(string warning)
    {
        if (_warningSink is null || Interlocked.Exchange(ref _warningQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _warningQueued, 0);
            _warningSink(warning);
        }, DispatcherPriority.Background);
    }
}
