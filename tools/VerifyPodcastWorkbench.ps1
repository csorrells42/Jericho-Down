param(
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
    dotnet build .\PodcastWorkbench.slnx
}

Invoke-GatedStep "Run test harness" {
    dotnet run --project .\tests\PodcastWorkbench.Tests\PodcastWorkbench.Tests.csproj
}

Invoke-GatedStep "List cameras" {
    dotnet run --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --list
}

if ($LiveCamera) {
    Invoke-GatedStep "Probe real camera through DX12 preview host" {
        dotnet run --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --mf-preview --dx12-preview --source mf --mode $Mode --camera $Camera --seconds $Seconds
    }

    Invoke-GatedStep "Probe virtual camera through DX12 preview host" {
        dotnet run --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --dx12-preview --source directshow --camera $VirtualCamera --seconds $Seconds
    }
}

if ($TextureDiagnostic) {
    Invoke-InformationalStep "Texture-native diagnostic" {
        dotnet run --project .\tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --texture --source mf --mode $Mode --camera $Camera --samples $TextureSamples --seconds ([Math]::Max($Seconds, 10))
    }
}

Write-Host ""
Write-Host "Podcast Workbench verification complete."
