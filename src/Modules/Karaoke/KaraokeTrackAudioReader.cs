using System.Buffers.Binary;
using System.IO;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JerichoDown.Modules.Karaoke;

public sealed class KaraokeTrackAudioReader : ISampleProvider, IDisposable
{
    private readonly WaveStream _stream;
    private readonly ISampleProvider _sampleProvider;
    private bool _disposed;

    private KaraokeTrackAudioReader(WaveStream stream, ISampleProvider sampleProvider)
    {
        _stream = stream;
        _sampleProvider = sampleProvider;
    }

    public static KaraokeTrackAudioReader Open(string path)
    {
        if (!CanUseSampleReader(path))
        {
            var extension = Path.GetExtension(path);
            throw new NotSupportedException($"{extension} backing tracks are not supported by the sample playback path.");
        }

        return OpenWaveStream(new AudioFileReader(path));
    }

    public static bool CanUseSampleReader(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wma", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aiff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aif", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryReadDuration(string path, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (!CanUseSampleReader(path))
        {
            return TryReadMpeg4Duration(path, out duration);
        }

        try
        {
            using var reader = Open(path);
            duration = reader.TotalTime;
            return duration > TimeSpan.Zero;
        }
        catch
        {
            return TryReadMpeg4Duration(path, out duration);
        }
    }

    private static bool TryReadMpeg4Duration(string path, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".mov", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return TryReadMpeg4DurationFromBoxes(stream, stream.Length, 0, out duration);
        }
        catch
        {
            duration = TimeSpan.Zero;
            return false;
        }
    }

    private static bool TryReadMpeg4DurationFromBoxes(Stream stream, long endPosition, int depth, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (depth > 6 || !stream.CanSeek || endPosition <= stream.Position)
        {
            return false;
        }

        var header = new byte[16];
        var atomCount = 0;
        while (stream.Position + 8 <= endPosition && atomCount++ < 4096)
        {
            var atomStart = stream.Position;
            if (stream.Read(header, 0, 8) != 8)
            {
                return false;
            }

            var atomSize = (long)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var atomType = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8L;
            if (atomSize == 1)
            {
                if (stream.Read(header, 8, 8) != 8)
                {
                    return false;
                }

                atomSize = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8)));
                headerSize = 16L;
            }
            else if (atomSize == 0)
            {
                atomSize = endPosition - atomStart;
            }

            if (atomSize < headerSize)
            {
                return false;
            }

            var atomEnd = Math.Min(endPosition, atomStart + atomSize);
            if (atomType.Equals("mvhd", StringComparison.Ordinal))
            {
                stream.Position = atomStart + headerSize;
                return TryReadMovieHeaderDuration(stream, atomEnd, out duration);
            }

            if (IsMpeg4ContainerAtom(atomType))
            {
                stream.Position = atomStart + headerSize;
                if (TryReadMpeg4DurationFromBoxes(stream, atomEnd, depth + 1, out duration))
                {
                    return true;
                }
            }

            stream.Position = atomEnd;
        }

        return false;
    }

    private static bool TryReadMovieHeaderDuration(Stream stream, long atomEnd, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        Span<byte> fullBoxHeader = stackalloc byte[4];
        if (atomEnd - stream.Position < 20 || stream.Read(fullBoxHeader) != 4)
        {
            return false;
        }

        var version = fullBoxHeader[0];
        if (version == 1)
        {
            Span<byte> buffer = stackalloc byte[28];
            if (atomEnd - stream.Position < buffer.Length || stream.Read(buffer) != buffer.Length)
            {
                return false;
            }

            var timescale = BinaryPrimitives.ReadUInt32BigEndian(buffer[16..20]);
            var rawDuration = BinaryPrimitives.ReadUInt64BigEndian(buffer[20..28]);
            return TryCreateDuration(timescale, rawDuration, out duration);
        }

        if (version == 0)
        {
            Span<byte> buffer = stackalloc byte[16];
            if (atomEnd - stream.Position < buffer.Length || stream.Read(buffer) != buffer.Length)
            {
                return false;
            }

            var timescale = BinaryPrimitives.ReadUInt32BigEndian(buffer[8..12]);
            var rawDuration = BinaryPrimitives.ReadUInt32BigEndian(buffer[12..16]);
            return TryCreateDuration(timescale, rawDuration, out duration);
        }

        return false;
    }

    private static bool TryCreateDuration(uint timescale, ulong rawDuration, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (timescale == 0 || rawDuration == 0)
        {
            return false;
        }

        var seconds = rawDuration / (double)timescale;
        if (!double.IsFinite(seconds) || seconds <= 0d || seconds > TimeSpan.FromDays(2).TotalSeconds)
        {
            return false;
        }

        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool IsMpeg4ContainerAtom(string atomType)
    {
        return atomType is "moov" or "trak" or "mdia" or "minf" or "stbl";
    }

    public WaveFormat WaveFormat => _sampleProvider.WaveFormat;

    public TimeSpan CurrentTime
    {
        get => _stream.CurrentTime;
        set => _stream.CurrentTime = value;
    }

    public TimeSpan TotalTime => _stream.TotalTime;

    public int Read(float[] buffer, int offset, int count)
    {
        return _sampleProvider.Read(buffer, offset, count);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }

    private static KaraokeTrackAudioReader OpenWaveStream(WaveStream stream)
    {
        try
        {
            return new KaraokeTrackAudioReader(stream, stream.ToSampleProvider());
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}

public sealed class KaraokeRateSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly float[] _currentFrame;
    private readonly float[] _nextFrame;
    private readonly float[] _readBuffer;
    private bool _hasCurrentFrame;
    private bool _hasNextFrame;
    private double _framePosition;
    private double _playbackRate;

    public KaraokeRateSampleProvider(ISampleProvider source, double playbackRate)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _currentFrame = new float[_channels];
        _nextFrame = new float[_channels];
        _readBuffer = new float[_channels];
        SetPlaybackRate(playbackRate);
        Reset();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void SetPlaybackRate(double playbackRate)
    {
        _playbackRate = Math.Clamp(playbackRate, 0.5d, 2d);
    }

    public void Reset()
    {
        _framePosition = 0d;
        _hasCurrentFrame = ReadFrame(_currentFrame);
        _hasNextFrame = ReadFrame(_nextFrame);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_hasCurrentFrame)
        {
            return 0;
        }

        var framesRequested = count / _channels;
        var framesWritten = 0;
        var writeIndex = offset;
        while (framesWritten < framesRequested && _hasCurrentFrame)
        {
            var fraction = _hasNextFrame ? _framePosition : 0d;
            for (var channel = 0; channel < _channels; channel++)
            {
                var current = _currentFrame[channel];
                var next = _hasNextFrame ? _nextFrame[channel] : current;
                buffer[writeIndex++] = (float)(current + (next - current) * fraction);
            }

            framesWritten++;
            _framePosition += _playbackRate;
            while (_framePosition >= 1d && _hasCurrentFrame)
            {
                _framePosition -= 1d;
                AdvanceFrame();
            }
        }

        return framesWritten * _channels;
    }

    private void AdvanceFrame()
    {
        if (!_hasNextFrame)
        {
            _hasCurrentFrame = false;
            return;
        }

        Array.Copy(_nextFrame, _currentFrame, _channels);
        _hasCurrentFrame = true;
        _hasNextFrame = ReadFrame(_nextFrame);
    }

    private bool ReadFrame(float[] target)
    {
        var read = _source.Read(_readBuffer, 0, _channels);
        if (read <= 0)
        {
            Array.Clear(target);
            return false;
        }

        for (var i = 0; i < _channels; i++)
        {
            target[i] = i < read ? _readBuffer[i] : 0f;
        }

        return true;
    }
}

public sealed class KaraokeVocalReductionSampleProvider(ISampleProvider source, bool enabled) : ISampleProvider
{
    private volatile bool _enabled = enabled;

    public WaveFormat WaveFormat => source.WaveFormat;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        if (!_enabled || WaveFormat.Channels < 2)
        {
            return read;
        }

        var channels = WaveFormat.Channels;
        var end = offset + read - channels + 1;
        for (var sample = offset; sample < end; sample += channels)
        {
            var left = buffer[sample];
            var right = buffer[sample + 1];
            var sideLeft = (left - right) * 0.72f;
            var sideRight = (right - left) * 0.72f;

            buffer[sample] = Math.Clamp(sideLeft, -1f, 1f);
            buffer[sample + 1] = Math.Clamp(sideRight, -1f, 1f);
        }

        return read;
    }
}
