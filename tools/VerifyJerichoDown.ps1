param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$UiSmoke,
    [switch]$AudioHardware,
    [switch]$LiveCamera,
    [switch]$TextureDiagnostic,
    [string]$Camera = "Insta360 Link 2 Pro",
    [string]$VirtualCamera = "Insta360 Virtual",
    [string]$Mode = "auto",
    [int]$Seconds = 4,
    [int]$TextureSamples = 12
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function Invoke-GatedStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "== $Name =="

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-InformationalStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "== $Name =="

    & $Command
    Write-Host "$Name completed with exit code $LASTEXITCODE."
}

Invoke-GatedStep "Shut down dotnet build servers" {
    dotnet build-server shutdown
}

Invoke-GatedStep "Build solution" {
    dotnet build .\JerichoDown.slnx -c $Configuration
}

Invoke-GatedStep "Run test harness" {
    dotnet run --no-build --project .\tests\JerichoDown.Tests\JerichoDown.Tests.csproj -c $Configuration
}

Invoke-GatedStep "List cameras" {
    dotnet run --no-build --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -c $Configuration -- --list
}

if ($UiSmoke) {
    Invoke-GatedStep "Exercise WPF workflows with an isolated profile" {
        dotnet run --no-build --project .\tools\ReleaseSmokeProbe\ReleaseSmokeProbe.csproj -c $Configuration
    }
}

if ($AudioHardware) {
    Invoke-GatedStep "Check connected audio inputs and speaker output" {
        dotnet run --no-build --project .\tools\ReleaseSmokeProbe\ReleaseSmokeProbe.csproj -c $Configuration -- --audio-hardware
    }
}

if ($LiveCamera) {
    Invoke-GatedStep "Probe real camera through DX12 preview host" {
        dotnet run --no-build --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -c $Configuration -- --mf-preview --dx12-preview --source mf --mode $Mode --camera $Camera --seconds $Seconds
    }

    Invoke-GatedStep "Probe virtual camera through DX12 preview host" {
        dotnet run --no-build --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -c $Configuration -- --dx12-preview --source directshow --camera $VirtualCamera --seconds $Seconds
    }
}

if ($TextureDiagnostic) {
    Invoke-InformationalStep "Texture-native diagnostic" {
        dotnet run --no-build --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -c $Configuration -- --texture --source mf --mode $Mode --camera $Camera --samples $TextureSamples --seconds ([Math]::Max($Seconds, 10))
    }
}

Write-Host ""
Write-Host "Jericho Down verification complete."
