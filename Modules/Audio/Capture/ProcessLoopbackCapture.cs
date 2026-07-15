using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace JerichoDown.Modules.Audio.Capture;

public sealed class ProcessLoopbackCapture : IWaveIn, IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";
    private const ushort VtBlob = 65;
    private const int CaptureBufferMilliseconds = 120;
    private readonly object _syncRoot = new();
    private readonly int _targetProcessId;
    private AudioClient? _audioClient;
    private AudioCaptureClient? _captureClient;
    private Thread? _captureThread;
    private byte[] _captureBuffer = [];
    private bool _stopRequested;
    private bool _isRecording;
    private bool _disposed;

    public ProcessLoopbackCapture(int targetProcessId)
    {
        if (targetProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetProcessId), "A process-loopback target must have a positive process ID.");
        }

        _targetProcessId = targetProcessId;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat { get; set; }

    public int TargetProcessId => _targetProcessId;

    public void StartRecording()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_isRecording)
            {
                return;
            }

            try
            {
                _stopRequested = false;
                _audioClient = ActivateProcessLoopbackAudioClient(_targetProcessId);
                WaveFormat = _audioClient.MixFormat;
                _audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.Loopback,
                    CaptureBufferMilliseconds * 10_000L,
                    0,
                    WaveFormat,
                    Guid.Empty);
                _captureClient = _audioClient.AudioCaptureClient;
                _audioClient.Start();
                _isRecording = true;
                _captureThread = new Thread(CaptureThread)
                {
                    IsBackground = true,
                    Name = $"Process loopback capture {_targetProcessId}"
                };
                _captureThread.Start();
            }
            catch
            {
                _isRecording = false;
                ReleaseAudioClient();
                throw;
            }
        }
    }

    public void StopRecording()
    {
        Thread? captureThread;
        lock (_syncRoot)
        {
            if (!_isRecording)
            {
                return;
            }

            _stopRequested = true;
            captureThread = _captureThread;
        }

        if (captureThread is not null && !ReferenceEquals(Thread.CurrentThread, captureThread))
        {
            captureThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        StopRecording();
        ReleaseAudioClient();
    }

    private void CaptureThread()
    {
        Exception? stopException = null;
        try
        {
            while (!Volatile.Read(ref _stopRequested))
            {
                var packetSize = _captureClient?.GetNextPacketSize() ?? 0;
                if (packetSize == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                while (packetSize > 0 && !Volatile.Read(ref _stopRequested))
                {
                    ReadNextPacket();
                    packetSize = _captureClient?.GetNextPacketSize() ?? 0;
                }
            }
        }
        catch (Exception ex)
        {
            stopException = ex;
        }
        finally
        {
            lock (_syncRoot)
            {
                _isRecording = false;
                _captureThread = null;
            }

            ReleaseAudioClient();
            RecordingStopped?.Invoke(this, new StoppedEventArgs(stopException));
        }
    }

    private void ReadNextPacket()
    {
        var captureClient = _captureClient;
        if (captureClient is null)
        {
            return;
        }

        var data = captureClient.GetBuffer(
            out var framesAvailable,
            out var bufferFlags,
            out _,
            out _);
        try
        {
            var byteCount = Math.Max(0, framesAvailable * WaveFormat.BlockAlign);
            if (byteCount == 0)
            {
                return;
            }

            if (_captureBuffer.Length < byteCount)
            {
                _captureBuffer = new byte[byteCount];
            }

            if ((bufferFlags & AudioClientBufferFlags.Silent) != 0 || data == IntPtr.Zero)
            {
                Array.Clear(_captureBuffer, 0, byteCount);
            }
            else
            {
                Marshal.Copy(data, _captureBuffer, 0, byteCount);
            }

            DataAvailable?.Invoke(this, new WaveInEventArgs(_captureBuffer, byteCount));
        }
        finally
        {
            captureClient.ReleaseBuffer(framesAvailable);
        }
    }

    private void ReleaseAudioClient()
    {
        lock (_syncRoot)
        {
            try
            {
                _audioClient?.Stop();
            }
            catch
            {
            }

            _captureClient?.Dispose();
            _audioClient?.Dispose();
            _captureClient = null;
            _audioClient = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProcessLoopbackCapture));
        }
    }

    private static AudioClient ActivateProcessLoopbackAudioClient(int targetProcessId)
    {
        var activationParams = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new ProcessLoopbackActivationParams
            {
                TargetProcessId = (uint)targetProcessId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree
            }
        };

        var activationParamsPointer = IntPtr.Zero;
        var propVariantPointer = IntPtr.Zero;
        try
        {
            activationParamsPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
            Marshal.StructureToPtr(activationParams, activationParamsPointer, fDeleteOld: false);

            var propVariant = new BlobPropVariant
            {
                VariantType = VtBlob,
                Blob = new PropVariantBlob
                {
                    Size = Marshal.SizeOf<AudioClientActivationParams>(),
                    Data = activationParamsPointer
                }
            };
            propVariantPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlobPropVariant>());
            Marshal.StructureToPtr(propVariant, propVariantPointer, fDeleteOld: false);

            var completionHandler = new ActivateAudioInterfaceCompletionHandler();
            var interfaceId = typeof(IAudioClient).GUID;
            var result = ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                ref interfaceId,
                propVariantPointer,
                completionHandler,
                out _);
            ThrowIfFailed(result);

            return completionHandler.GetAudioClient();
        }
        finally
        {
            if (propVariantPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(propVariantPointer);
            }

            if (activationParamsPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(activationParamsPointer);
            }
        }
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        ref Guid interfaceId,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;

        public ProcessLoopbackActivationParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessLoopbackActivationParams
    {
        public uint TargetProcessId;

        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    private enum AudioClientActivationType
    {
        Default = 0,
        ProcessLoopback = 1
    }

    private enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree = 0,
        ExcludeTargetProcessTree = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlob
    {
        public int Size;

        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct BlobPropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public PropVariantBlob Blob;
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(
            out int activateResult,
            [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComVisible(true)]
    private sealed class ActivateAudioInterfaceCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEventSlim _completed = new();
        private object? _activatedInterface;
        private Exception? _exception;
        private int _activateResult;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                var result = activateOperation.GetActivateResult(out _activateResult, out _activatedInterface);
                ThrowIfFailed(result);
            }
            catch (Exception ex)
            {
                _exception = ex;
            }
            finally
            {
                _completed.Set();
            }

            return 0;
        }

        public AudioClient GetAudioClient()
        {
            if (!_completed.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Windows did not complete process-loopback activation.");
            }

            if (_exception is not null)
            {
                throw new InvalidOperationException("Windows process-loopback activation failed.", _exception);
            }

            ThrowIfFailed(_activateResult);
            if (_activatedInterface is not IAudioClient audioClient)
            {
                throw new InvalidOperationException("Windows process-loopback activation did not return an audio client.");
            }

            return new AudioClient(audioClient);
        }
    }
}
