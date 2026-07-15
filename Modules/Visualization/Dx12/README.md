# Visualization.Dx12 Module

Owns DX12 rendering for audio graphs.

Current legacy files:
- `Visualization/Direct3D12AudioGraphHost.cs`

Responsibilities:
- Render spectrum, waveform, and waterfall graph modes.
- Clear retained graph history when capture stops or no callbacks arrive.
- Keep graph mode behavior aligned with selected mic and mixer roles.

Do not put camera preview rendering here.
