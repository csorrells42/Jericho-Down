# Audio.Asio Module

Owns ASIO-specific capture and diagnostics.

Current responsibilities:
- Enumerate ASIO inputs without pre-opening drivers.
- Open live ASIO input using the proven record-only mode.
- Run the standalone callback probe.
- Keep ASIO objects on the STA dispatcher with a Windows message pump.
- Produce diagnostics that distinguish no-callback driver failures from silent input.

Current legacy files:
- `Audio/AsioInputCapture.cs`
- `Audio/AsioCallbackProbe.cs`
- `Audio/StaThreadDispatcher.cs`
- ASIO sections of `Audio/MicrophoneSpectrumService.cs`
- ASIO sections of `Audio/AudioDeviceDiagnostics.cs`

Do not add generic WASAPI, WaveIn, mixer, or DSP code here.
