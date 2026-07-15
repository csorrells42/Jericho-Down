# Webcam.Dx11Bridge Module

Owns bridge code for shared D3D11/D3D12 camera texture interop.

Current entry points:
- `Direct3D11DeviceManager.cs`
- `Direct3D11SharedTextureBridge.cs`

Temporary dependencies:
- `JerichoDown.Video.ITextureNativeDeviceManager` until the DX12 texture-native abstractions move into `Webcam/Dx12`.

Keep this module narrow. It should only exist to move compatible texture handles between capture and rendering paths.
