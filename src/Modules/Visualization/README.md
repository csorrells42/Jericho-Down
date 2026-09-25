# Visualization Module

Owns spectrum, waveform, waterfall, and graph data rendering outside the camera preview surface.

Responsibilities:
- Spectrum frame routing.
- Waveform and spectrum graph data.
- Feedback danger visualization.
- Graph clear/history behavior when audio callbacks stop.

Current entry points:
- `SpectrumAnalyzer.cs`
- `SpectrumFrame.cs`
- `SpectrumFrameRouter.cs`
- `FeedbackDangerDetector.cs`

Important submodules:
- `Dx12`: DX12 audio graph host and retained graph history.

Do not put camera preview DX12 rendering here. Camera preview belongs in `Webcam/Dx12`.
