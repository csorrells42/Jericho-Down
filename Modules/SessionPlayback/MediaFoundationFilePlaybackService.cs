using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using JerichoDown.Modules.Webcam;
using JerichoDown.Video;

namespace JerichoDown.Modules.SessionPlayback;

public sealed class MediaFoundationFilePlaybackService : IDisposable
{
    private const int VariantTypeInt64 = 20;
    private const int VariantTypeUInt64 = 21;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private readonly object _stateLock = new();
    private readonly ManualResetEventSlim _resumeEvent = new(true);
    private readonly Stopwatch _playbackClock = new();
    private CancellationTokenSource? _cancellation;
    private Task? _playbackTask;
    private IMFSourceReader? _reader;
    private MediaFoundationCameraDeviceFactory.MediaFoundationScope? _mediaFoundationScope;
    private TimeSpan _duration;
    private long _playbackBaseTicks;
    private long? _pendingSeekTicks;
    private bool _paused;
    private bool _disposed;

    public event EventHandler<CameraFrame>? FrameAvailable;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<string>? StatusChanged;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _playbackTask is not null && !_playbackTask.IsCompleted && _cancellation is not null;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_stateLock)
            {
                return _paused;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_stateLock)
            {
                return _duration;
            }
        }
    }

    public TimeSpan Position => TimeSpan.FromTicks(Math.Max(0L, GetCurrentPlaybackTicks()));

    public bool Start(string path, TimeSpan startPosition = default)
    {
        Stop();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!OperatingSystem.IsWindows())
        {
            StatusChanged?.Invoke(this, "DX12 session playback requires Windows Media Foundation.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusChanged?.Invoke(this, "DX12 session playback file was not found.");
            return false;
        }

        var cancellation = new CancellationTokenSource();
        var startup = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock)
        {
            _cancellation = cancellation;
            _duration = TimeSpan.Zero;
            _playbackBaseTicks = Math.Max(0L, startPosition.Ticks);
            _pendingSeekTicks = _playbackBaseTicks > 0 ? _playbackBaseTicks : null;
            _paused = false;
            _resumeEvent.Set();
            _playbackClock.Restart();
            _playbackTask = Task.Run(() => PlaybackLoop(path, cancellation.Token, startup));
        }

        if (!startup.Task.Wait(StartupTimeout))
        {
            Stop();
            StatusChanged?.Invoke(this, $"DX12 session playback timed out opening {Path.GetFileName(path)}.");
            return false;
        }

        var startupError = startup.Task.Result;
        if (!string.IsNullOrWhiteSpace(startupError))
        {
            Stop();
            StatusChanged?.Invoke(this, $"DX12 session playback unavailable: {startupError}");
            return false;
        }

        return true;
    }

    public void Play()
    {
        lock (_stateLock)
        {
            if (_cancellation is null)
            {
                return;
            }

            if (_paused)
            {
                _paused = false;
                _playbackClock.Restart();
                _resumeEvent.Set();
            }
        }
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (_cancellation is null || _paused)
            {
                return;
            }

            _playbackBaseTicks = GetCurrentPlaybackTicksLocked();
            _playbackClock.Reset();
            _paused = true;
            _resumeEvent.Reset();
        }
    }

    public void Seek(TimeSpan position)
    {
        var ticks = Math.Max(0L, position.Ticks);
        lock (_stateLock)
        {
            _pendingSeekTicks = ticks;
            _playbackBaseTicks = ticks;
            if (!_paused)
            {
                _playbackClock.Restart();
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task? playbackTask;
        IMFSourceReader? reader;
        lock (_stateLock)
        {
            cancellation = _cancellation;
            playbackTask = _playbackTask;
            reader = _reader;
            _cancellation = null;
            _playbackTask = null;
            _paused = false;
            _pendingSeekTicks = null;
            _playbackClock.Reset();
            _resumeEvent.Set();
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
            try
            {
                reader?.Flush(MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            }
            catch
            {
            }

            try
            {
                playbackTask?.Wait(StopTimeout);
            }
            catch
            {
            }

            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _resumeEvent.Dispose();
    }

    private void PlaybackLoop(string path, CancellationToken cancellationToken, TaskCompletionSource<string?> startup)
    {
        IMFSourceReader? reader = null;
        MediaFoundationCameraDeviceFactory.MediaFoundationScope? mediaFoundationScope = null;
        try
        {
            mediaFoundationScope = MediaFoundationCameraDeviceFactory.Startup();
            reader = CreateSourceReader(path);
            var format = ReadCurrentFormat(reader);
            var duration = ReadDuration(reader);
            lock (_stateLock)
            {
                _reader = reader;
                _mediaFoundationScope = mediaFoundationScope;
                _duration = duration;
            }

            StatusChanged?.Invoke(
                this,
                $"DX12 session playback format: {format.Width}x{format.Height}@{format.FramesPerSecond:0.###} {MediaFoundationInterop.FormatSubtype(format.Subtype)}, stride {format.Stride}.");
            startup.TrySetResult(null);

            var ended = false;
            var frameDurationTicks = GetFrameDurationTicks(format.FramesPerSecond);
            long? sourceTimestampOffsetTicks = null;
            var lastPresentationTicks = Math.Max(-1L, _playbackBaseTicks - frameDurationTicks);
            var syntheticPresentationTicks = Math.Max(0L, _playbackBaseTicks);
            while (!cancellationToken.IsCancellationRequested)
            {
                var appliedSeekTicks = ApplyPendingSeek(reader);
                if (appliedSeekTicks.HasValue)
                {
                    sourceTimestampOffsetTicks = null;
                    lastPresentationTicks = Math.Max(-1L, appliedSeekTicks.Value - frameDurationTicks);
                    syntheticPresentationTicks = Math.Max(0L, appliedSeekTicks.Value);
                }

                _resumeEvent.Wait(cancellationToken);

                var result = reader.ReadSample(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out _,
                    out var streamFlags,
                    out var timestamp,
                    out var sampleObject);

                if (MediaFoundationInterop.Failed(result))
                {
                    throw new InvalidOperationException($"ReadSample failed: 0x{result:X8}");
                }

                if ((streamFlags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    ended = true;
                    break;
                }

                if (sampleObject is not IMFSample sample)
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                    continue;
                }

                try
                {
                    var frameTicks = ResolvePresentationTicks(
                        timestamp,
                        frameDurationTicks,
                        ref sourceTimestampOffsetTicks,
                        ref lastPresentationTicks,
                        ref syntheticPresentationTicks);
                    WaitUntilFrameIsDue(frameTicks, cancellationToken);
                    if (TryReadFrame(sample, format.Width, format.Height, format.Subtype, format.Stride, out var frame))
                    {
                        FrameAvailable?.Invoke(this, frame);
                    }
                }
                finally
                {
                    MediaFoundationInterop.ReleaseComObject(sampleObject);
                }
            }

            if (ended && !cancellationToken.IsCancellationRequested)
            {
                lock (_stateLock)
                {
                    if (_duration > TimeSpan.Zero)
                    {
                        _playbackBaseTicks = _duration.Ticks;
                    }
                }

                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!startup.Task.IsCompleted)
            {
                startup.TrySetResult(ex.Message);
            }
            else
            {
                PlaybackFailed?.Invoke(this, ex.Message);
            }
        }
        finally
        {
            if (!startup.Task.IsCompleted)
            {
                startup.TrySetResult("playback loop ended before startup completed");
            }

            lock (_stateLock)
            {
                if (ReferenceEquals(_reader, reader))
                {
                    _reader = null;
                }

                if (ReferenceEquals(_mediaFoundationScope, mediaFoundationScope))
                {
                    _mediaFoundationScope = null;
                }
            }

            MediaFoundationInterop.ReleaseComObject(reader);
            mediaFoundationScope?.Dispose();
        }
    }

    private static IMFSourceReader CreateSourceReader(string path)
    {
        MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateAttributes(out var attributes, 3));
        try
        {
            MediaFoundationInterop.ThrowIfFailed(attributes.SetUINT32(
                MediaFoundationGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
                1));
            MediaFoundationInterop.ThrowIfFailed(attributes.SetUINT32(
                MediaFoundationGuids.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING,
                1));

            var result = MediaFoundationInterop.MFCreateSourceReaderFromURL(path, attributes, out var reader);
            if (MediaFoundationInterop.Failed(result))
            {
                throw new InvalidOperationException($"source reader creation failed: 0x{result:X8}");
            }

            try
            {
                _ = reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ANY_STREAM, false);
                MediaFoundationInterop.ThrowIfFailed(reader.SetStreamSelection(
                    MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    true));
                ConfigureVideoOutput(reader);
                return reader;
            }
            catch
            {
                MediaFoundationInterop.ReleaseComObject(reader);
                throw;
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(attributes);
        }
    }

    private static void ConfigureVideoOutput(IMFSourceReader reader)
    {
        if (TrySetVideoSubtype(reader, MediaFoundationGuids.MFVideoFormat_RGB32))
        {
            return;
        }

        if (TrySetVideoSubtype(reader, MediaFoundationGuids.MFVideoFormat_NV12))
        {
            return;
        }

        throw new InvalidOperationException("Media Foundation could not decode this file to RGB32 or NV12 video.");
    }

    private static bool TrySetVideoSubtype(IMFSourceReader reader, Guid subtype)
    {
        IMFMediaType? mediaType = null;
        try
        {
            MediaFoundationInterop.ThrowIfFailed(MediaFoundationInterop.MFCreateMediaType(out mediaType));
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
                MediaFoundationGuids.MF_MT_MAJOR_TYPE,
                MediaFoundationGuids.MFMediaType_Video));
            MediaFoundationInterop.ThrowIfFailed(mediaType.SetGUID(
                MediaFoundationGuids.MF_MT_SUBTYPE,
                subtype));
            return !MediaFoundationInterop.Failed(reader.SetCurrentMediaType(
                MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                IntPtr.Zero,
                mediaType));
        }
        catch
        {
            return false;
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(mediaType);
        }
    }

    private static PlaybackVideoFormat ReadCurrentFormat(IMFSourceReader reader)
    {
        var result = reader.GetCurrentMediaType(
            MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            out var currentType);
        if (MediaFoundationInterop.Failed(result))
        {
            throw new InvalidOperationException($"current video type unavailable: 0x{result:X8}");
        }

        try
        {
            var width = 1280;
            var height = 720;
            var framesPerSecond = 30d;
            var subtype = MediaFoundationGuids.MFVideoFormat_RGB32;
            if (MediaFoundationInterop.TryGetFrameSize(currentType, out var activeWidth, out var activeHeight))
            {
                width = activeWidth;
                height = activeHeight;
            }

            if (MediaFoundationInterop.TryGetFrameRate(currentType, out var activeFps))
            {
                framesPerSecond = activeFps;
            }

            if (!MediaFoundationInterop.Failed(currentType.GetGUID(MediaFoundationGuids.MF_MT_SUBTYPE, out var activeSubtype)))
            {
                subtype = activeSubtype;
            }

            var stride = subtype == MediaFoundationGuids.MFVideoFormat_RGB32 ? width * 4 : width;
            if (!MediaFoundationInterop.Failed(currentType.GetUINT32(MediaFoundationGuids.MF_MT_DEFAULT_STRIDE, out var activeStride)))
            {
                stride = Math.Abs(activeStride);
            }

            return new PlaybackVideoFormat(width, height, framesPerSecond, subtype, stride);
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(currentType);
        }
    }

    private static TimeSpan ReadDuration(IMFSourceReader reader)
    {
        var variantPointer = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(default(PropVariant), variantPointer, false);
            var result = reader.GetPresentationAttribute(
                MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                MediaFoundationGuids.MF_PD_DURATION,
                variantPointer);
            if (MediaFoundationInterop.Failed(result))
            {
                return TimeSpan.Zero;
            }

            var variant = Marshal.PtrToStructure<PropVariant>(variantPointer);
            return variant.VariantType is VariantTypeInt64 or VariantTypeUInt64 && variant.Value > 0
                ? TimeSpan.FromTicks(variant.Value)
                : TimeSpan.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(variantPointer);
        }
    }

    private static bool TryReadFrame(
        IMFSample sample,
        int width,
        int height,
        Guid subtype,
        int stride,
        out CameraFrame frame)
    {
        frame = new CameraFrame([], 0, 0, 0);
        IMFMediaBuffer? buffer = null;
        try
        {
            var result = sample.GetBufferByIndex(0, out buffer);
            if (MediaFoundationInterop.Failed(result))
            {
                MediaFoundationInterop.ThrowIfFailed(sample.ConvertToContiguousBuffer(out buffer));
            }

            MediaFoundationInterop.ThrowIfFailed(buffer.Lock(out var source, out _, out var currentLength));
            try
            {
                if (subtype == MediaFoundationGuids.MFVideoFormat_NV12)
                {
                    var nv12Stride = stride > 0 ? stride : width;
                    var expectedNv12Bytes = nv12Stride * height + nv12Stride * ((height + 1) / 2);
                    if (currentLength < expectedNv12Bytes)
                    {
                        return false;
                    }

                    var nv12Bytes = new byte[expectedNv12Bytes];
                    Marshal.Copy(source, nv12Bytes, 0, expectedNv12Bytes);
                    frame = new CameraFrame([], width, height, 0, nv12Bytes, nv12Stride, "nv12");
                    return true;
                }

                var bgraStride = stride > 0 ? stride : width * 4;
                var expectedBytes = bgraStride * height;
                if (currentLength < expectedBytes)
                {
                    return false;
                }

                var bytes = new byte[expectedBytes];
                Marshal.Copy(source, bytes, 0, expectedBytes);
                frame = new CameraFrame(bytes, width, height, bgraStride);
                return true;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            MediaFoundationInterop.ReleaseComObject(buffer);
        }
    }

    private long? ApplyPendingSeek(IMFSourceReader reader)
    {
        long? pendingSeek;
        lock (_stateLock)
        {
            pendingSeek = _pendingSeekTicks;
            _pendingSeekTicks = null;
        }

        if (!pendingSeek.HasValue)
        {
            return null;
        }

        using var variant = PropVariantHandle.FromInt64(pendingSeek.Value);
        MediaFoundationInterop.ThrowIfFailed(reader.SetCurrentPosition(Guid.Empty, variant.Pointer));
        return pendingSeek.Value;
    }

    private static long GetFrameDurationTicks(double framesPerSecond)
    {
        var fps = double.IsFinite(framesPerSecond)
            ? Math.Clamp(framesPerSecond, 1d, 120d)
            : 30d;
        return Math.Max(1L, (long)Math.Round(TimeSpan.TicksPerSecond / fps));
    }

    private static long ResolvePresentationTicks(
        long sourceTimestampTicks,
        long frameDurationTicks,
        ref long? sourceTimestampOffsetTicks,
        ref long lastPresentationTicks,
        ref long syntheticPresentationTicks)
    {
        if (sourceTimestampTicks >= 0)
        {
            sourceTimestampOffsetTicks ??= sourceTimestampTicks - syntheticPresentationTicks;
            var presentationTicks = Math.Max(0L, sourceTimestampTicks - sourceTimestampOffsetTicks.Value);
            var sourceDeltaTicks = presentationTicks - lastPresentationTicks;
            var minimumSourceDeltaTicks = Math.Max(TimeSpan.TicksPerMillisecond * 2, frameDurationTicks / 4);
            if (lastPresentationTicks < 0 || sourceDeltaTicks >= minimumSourceDeltaTicks)
            {
                lastPresentationTicks = presentationTicks;
                syntheticPresentationTicks = presentationTicks + frameDurationTicks;
                return presentationTicks;
            }
        }

        var syntheticTicks = syntheticPresentationTicks;
        if (syntheticTicks <= lastPresentationTicks)
        {
            syntheticTicks = lastPresentationTicks + frameDurationTicks;
        }

        lastPresentationTicks = syntheticTicks;
        syntheticPresentationTicks = syntheticTicks + frameDurationTicks;
        return syntheticTicks;
    }

    private void WaitUntilFrameIsDue(long frameTicks, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _resumeEvent.Wait(cancellationToken);
            var delayTicks = frameTicks - GetCurrentPlaybackTicks();
            if (delayTicks <= 0)
            {
                return;
            }

            Thread.Sleep((int)Math.Clamp(delayTicks / TimeSpan.TicksPerMillisecond, 1L, 16L));
        }
    }

    private long GetCurrentPlaybackTicks()
    {
        lock (_stateLock)
        {
            return GetCurrentPlaybackTicksLocked();
        }
    }

    private long GetCurrentPlaybackTicksLocked()
    {
        if (_paused || !_playbackClock.IsRunning)
        {
            return _playbackBaseTicks;
        }

        var ticks = _playbackBaseTicks + _playbackClock.Elapsed.Ticks;
        return _duration > TimeSpan.Zero ? Math.Min(ticks, _duration.Ticks) : ticks;
    }

    private sealed record PlaybackVideoFormat(
        int Width,
        int Height,
        double FramesPerSecond,
        Guid Subtype,
        int Stride);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public long Value;
        public long ReservedValue;
    }

    private sealed class PropVariantHandle : IDisposable
    {
        private IntPtr _pointer;

        private PropVariantHandle(IntPtr pointer)
        {
            _pointer = pointer;
        }

        public IntPtr Pointer => _pointer;

        public static PropVariantHandle FromInt64(long value)
        {
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
            Marshal.StructureToPtr(
                new PropVariant
                {
                    VariantType = VariantTypeInt64,
                    Value = value
                },
                pointer,
                false);
            return new PropVariantHandle(pointer);
        }

        public void Dispose()
        {
            var pointer = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
