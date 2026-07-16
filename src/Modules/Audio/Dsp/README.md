# Audio.Dsp Module

Owns microphone voice processing, EQ, presets, verification, and signal-shaping math.

Current entry points:
- `DspVerificationReportGenerator.cs`
- `GraphicEqualizerProcessor.cs`
- `GraphicEqualizerSettings.cs`
- `GraphicEqualizerResponse.cs`
- `GraphicEqualizerVerification.cs`
- `VoiceProcessorSettings.cs`
- `BuiltInVoicePresetCatalog.cs`
- `VoiceProcessingTelemetry.cs`
- `EqualizerBand.cs`
- `VoiceSampleProcessor.cs`
- `VoiceProcessorSampleProvider.cs`
- `StereoVoiceProcessorSampleProvider.cs`
- `NAudioBiQuadFilterRack.cs`
- `NAudioPitchShiftProcessor.cs`
- `NAudioImpulseConvolutionProcessor.cs`
- `NAudioEnvelopeGeneratorProcessor.cs`
- `NAudioDmoEffectChain.cs`

Consumed by:
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` for live capture, graphing, and recording pipelines.

Graphic EQ notes:
- `GraphicEqualizerProcessor` is the reusable zero-algorithmic-latency EQ engine used by `VoiceSampleProcessor`.
- The processor uses flat arrays, active-band indexes, preallocated state, and `Span<T>` block processing so the steady audio path does not allocate while monitoring.
- `GraphicEqualizerVerification` measures isolated slider response and complete multi-slider curves against the modeled biquad response.

Keep NAudio-owned effect verification separate from custom Jericho DSP claims.
