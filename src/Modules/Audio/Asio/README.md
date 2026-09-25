# Audio.Asio Module

Owns ASIO-specific capture and diagnostics.

Current responsibilities:
- Enumerate ASIO inputs without pre-opening drivers.
- Open live ASIO input using the proven record-only mode.
- Run the standalone callback probe.
- Keep ASIO objects on the STA dispatcher with a Windows message pump.
- Produce diagnostics that distinguish no-callback driver failures from silent input.

Current entry points:
- `AsioInputCapture.cs`
- `AsioCallbackProbe.cs`
- `AsioOutputPlayer.cs`
- `StaThreadDispatcher.cs`

Current integration points:
- ASIO sections of `Modules/Audio/Live/MicrophoneSpectrumService.cs`
- ASIO sections of `Modules/Audio/Diagnostics/AudioDeviceDiagnostics.cs`

Do not add generic WASAPI, WaveIn, mixer, or DSP code here.
