using System.Buffers;

namespace JerichoDown.Audio;

public sealed class AudioSyncBuffer
{
    private readonly object _lock = new();
    private readonly int _targetSampleRate;
    private readonly int _targetLatencySamples;
    private readonly int _maximumBufferedSamples;
    private readonly float[] _buffer;
    private int _readIndex;
    private int _writeIndex;
    private int _count;

    public AudioSyncBuffer(int targetSampleRate, TimeSpan targetLatency, TimeSpan maximumLatency)
    {
        _targetSampleRate = Math.Max(8000, targetSampleRate);
        _targetLatencySamples = DurationToSampleCount(targetLatency, _targetSampleRate);
        _maximumBufferedSamples = Math.Max(
            _targetLatencySamples + 1,
            DurationToSampleCount(maximumLatency, _targetSampleRate));
        _buffer = new float[Math.Max(_maximumBufferedSamples * 2, _targetSampleRate / 2)];
    }

    public int TargetLatencySamples => _targetLatencySamples;

    public int BufferedSamples
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public int UnderflowCount { get; private set; }

    public int DriftTrimCount { get; private set; }

    public void Clear()
    {
        lock (_lock)
        {
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
            UnderflowCount = 0;
            DriftTrimCount = 0;
        }
    }

    public void Write(ReadOnlySpan<float> samples, int sourceSampleRate)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        if (sourceSampleRate <= 0 || sourceSampleRate == _targetSampleRate)
        {
            Append(samples);
            return;
        }

        var outputLength = Math.Max(1, (int)Math.Round(samples.Length * (double)_targetSampleRate / sourceSampleRate));
        var resampled = ArrayPool<float>.Shared.Rent(outputLength);
        try
        {
            ResampleLinear(samples, sourceSampleRate, resampled.AsSpan(0, outputLength), _targetSampleRate);
            Append(resampled.AsSpan(0, outputLength));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(resampled);
        }
    }

    public bool ReadAligned(Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return true;
        }

        lock (_lock)
        {
            var requiredSamples = _targetLatencySamples + destination.Length;
            if (_count < requiredSamples)
            {
                destination.Clear();
                UnderflowCount++;
                return false;
            }

            if (_count > _maximumBufferedSamples)
            {
                var keepSamples = Math.Max(destination.Length, requiredSamples);
                Discard(_count - keepSamples);
                DriftTrimCount++;
            }

            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] = _buffer[_readIndex];
                _readIndex = (_readIndex + 1) % _buffer.Length;
            }

            _count -= destination.Length;
            return true;
        }
    }

    internal static void ResampleLinear(
        ReadOnlySpan<float> source,
        int sourceSampleRate,
        Span<float> destination,
        int targetSampleRate)
    {
        if (source.IsEmpty || destination.IsEmpty)
        {
            return;
        }

        if (sourceSampleRate <= 0 || sourceSampleRate == targetSampleRate)
        {
            source[..Math.Min(source.Length, destination.Length)].CopyTo(destination);
            if (destination.Length > source.Length)
            {
                destination[source.Length..].Clear();
            }

            return;
        }

        var sourceStep = sourceSampleRate / (double)Math.Max(1, targetSampleRate);
        for (var i = 0; i < destination.Length; i++)
        {
            var sourcePosition = i * sourceStep;
            var sourceIndex = (int)sourcePosition;
            if (sourceIndex >= source.Length - 1)
            {
                destination[i] = SanitizeSample(source[^1]);
                continue;
            }

            var fraction = sourcePosition - sourceIndex;
            var a = SanitizeSample(source[sourceIndex]);
            var b = SanitizeSample(source[sourceIndex + 1]);
            destination[i] = (float)(a + (b - a) * fraction);
        }
    }

    private void Append(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var sample in samples)
            {
                _buffer[_writeIndex] = SanitizeSample(sample);
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                if (_count == _buffer.Length)
                {
                    _readIndex = (_readIndex + 1) % _buffer.Length;
                    DriftTrimCount++;
                }
                else
                {
                    _count++;
                }
            }
        }
    }

    private void Discard(int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return;
        }

        var discardCount = Math.Min(sampleCount, _count);
        _readIndex = (_readIndex + discardCount) % _buffer.Length;
        _count -= discardCount;
    }

    private static int DurationToSampleCount(TimeSpan duration, int sampleRate)
    {
        return Math.Max(0, (int)Math.Round(Math.Max(0d, duration.TotalSeconds) * sampleRate));
    }

    private static float SanitizeSample(float sample)
    {
        return Math.Clamp(float.IsFinite(sample) ? sample : 0f, -1f, 1f);
    }
}
