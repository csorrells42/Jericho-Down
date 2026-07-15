# Webcam.Dx11Bridge Module

Owns bridge code for shared D3D11/D3D12 camera texture interop.

Current legacy files:
- `Video/Direct3D11DeviceManager.cs`
- `Video/Direct3D11SharedTextureBridge.cs`

Keep this module narrow. It should only exist to move compatible texture handles between capture and rendering paths.
