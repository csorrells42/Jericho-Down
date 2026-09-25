# Audio.Sync Module

Owns latency, delay, and sample-rate alignment helpers for live audio.

Current entry points:
- `AudioDelayLine.cs`
- `AudioStereoDelayLine.cs`
- `AudioSyncBuffer.cs`
- `NAudioSampleRateConverter.cs`

Consumed by:
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` when aligning auxiliary captures and applying per-channel delay.

Do not put device enumeration, ASIO driver startup, or mix-bus policy here.
