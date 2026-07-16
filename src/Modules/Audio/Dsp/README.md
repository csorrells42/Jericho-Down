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

Keep NAudio-owned effect verification separate from custom Jericho DSP claims.
