# Audio.Dsp Module

Owns microphone voice processing, EQ, presets, verification, and signal-shaping math.

Current entry points:
- `DspVerificationReportGenerator.cs`
- `VoiceProcessorSettings.cs`
- `BuiltInVoicePresetCatalog.cs`
- `VoiceProcessingTelemetry.cs`
- `EqualizerBand.cs`

Temporary dependencies:
- `JerichoDown.Audio` voice processor and effect types until the rest of the DSP stack moves into this module.

Current legacy files include:
- `Audio/VoiceSampleProcessor.cs`
- `Audio/VoiceProcessorSampleProvider.cs`
- `Audio/NAudioBiQuadFilterRack.cs`
- `Audio/NAudioPitchShiftProcessor.cs`
- `Audio/NAudioImpulseConvolutionProcessor.cs`
- `Audio/NAudioEnvelopeGeneratorProcessor.cs`
- `Audio/NAudioDmoEffectChain.cs`

Keep NAudio-owned effect verification separate from custom Jericho DSP claims.
