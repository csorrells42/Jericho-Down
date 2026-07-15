# Visualization.Dx12 Module

Owns DX12 rendering for audio graphs.

Current entry points:
- `Direct3D12AudioGraphHost.cs`
- `VisualizationDirectX12ViewportHost.cs`

Responsibilities:
- Render spectrum, waveform, and waterfall graph modes.
- Clear retained graph history when capture stops or no callbacks arrive.
- Keep graph mode behavior aligned with selected mic and mixer roles.
- Own the visualization-local WPF child-window viewport host so the audio graph renderer is self-contained.

Do not put camera preview rendering here.
