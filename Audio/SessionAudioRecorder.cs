using System.IO;
using NAudio.Wave;

namespace JerichoDown.Audio;

internal sealed class SessionAudioRecorder
{
    private readonly object _sync = new();
    private WaveFileWriter? _mixWriter;
    private WaveFileWriter? _rawWriter;
    private bool _paused;
    private byte[] _mixBytes = [];
    private byte[] _rawBytes = [];
    public string? LastError { get; private set; }
    public bool IsRecording { get { lock (_sync) return _rawWriter is not null; } }

    public void Start(string? mixPath, string rawPath, WaveFormat inputFormat)
    {
        lock (_sync)
        {
            if (_rawWriter is not null) throw new InvalidOperationException("Session audio is already recording.");
            LastError = null;
            try
            {
                _rawWriter = CreateWriter(rawPath, inputFormat);
                if (mixPath is not null)
                    _mixWriter = CreateWriter(mixPath, WaveFormat.CreateIeeeFloatWaveFormat(inputFormat.SampleRate, 2));
                _paused = false;
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }

    public void SetPaused(bool paused) { lock (_sync) _paused = paused; }

    public string? Write(ReadOnlySpan<float> mix, int channels, ReadOnlySpan<byte> raw, WaveFormat inputFormat)
    {
        lock (_sync)
        {
            if (_rawWriter is null || _paused) return null;
            try
            {
                var expected = _rawWriter.WaveFormat;
                if (inputFormat.SampleRate != expected.SampleRate || inputFormat.Channels != expected.Channels
                    || inputFormat.BitsPerSample != expected.BitsPerSample || inputFormat.Encoding != expected.Encoding)
                    throw new InvalidOperationException("The input format changed during session recording.");
                if (_rawBytes.Length < raw.Length) _rawBytes = new byte[raw.Length];
                raw.CopyTo(_rawBytes);
                _rawWriter.Write(_rawBytes, 0, raw.Length);
                if (_mixWriter is not null)
                {
                    var count = ProcessedAudioSampleConverter.GetStereoFloat32ByteCount(mix.Length, channels);
                    if (_mixBytes.Length < count) _mixBytes = new byte[count];
                    ProcessedAudioSampleConverter.WriteStereoFloat32(mix, channels, _mixBytes);
                    _mixWriter.Write(_mixBytes, 0, count);
                }
                return null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Stop();
                return LastError;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            var mix = _mixWriter;
            var raw = _rawWriter;
            _mixWriter = null;
            _rawWriter = null;
            _paused = false;
            Close(mix);
            Close(raw);
        }
    }

    private void Close(WaveFileWriter? writer)
    {
        try { writer?.Dispose(); }
        catch (Exception ex)
        {
            LastError ??= ex.Message;
            AppStateStore.LogDiagnostic("session-audio-close-failed", ex);
        }
    }

    private static WaveFileWriter CreateWriter(string path, WaveFormat format)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try { return new WaveFileWriter(stream, format); }
        catch { stream.Dispose(); throw; }
    }
}
