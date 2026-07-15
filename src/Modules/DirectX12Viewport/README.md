# DirectX12Viewport Module

Owns reusable WPF viewport plumbing for Direct3D 12 rendering.

Responsibilities:
- Create and destroy the child HWND that a DX12 swap chain renders into.
- Forward WPF size changes to the child viewport.
- Keep viewport lifecycle code separate from audio graph, camera preview, or other visual content.
- Provide a small base class that another WPF program can subclass to host its own DX12 renderer.

Current entry points:
- `DirectX12ViewportHost.cs`

Do not put audio graph drawing, camera frame upload, denoise shaders, or Jericho Down window/menu wiring here.
