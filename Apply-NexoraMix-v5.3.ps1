param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$Solution = Join-Path $ProjectRoot "NexoraMixStudio.sln"
if (-not (Test-Path $Solution)) {
    throw "NexoraMixStudio.sln non trovato in: $ProjectRoot"
}

$Payload = Join-Path $PSScriptRoot "Payload"
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$BackupRoot = Join-Path $ProjectRoot ".artifacts\backup-v5.3-$Timestamp"

$Files = @(
    "NexoraMix.Core\Models\AudioTrack.cs",
    "NexoraMix.Audio\Playback\DeckChannel.cs",
    "NexoraMix.Audio\Playback\MasterAudioEngine.cs",
    "NexoraMix.App\Services\AppSettings.cs",
    "NexoraMix.App\Services\SpotifyAuthService.cs",
    "NexoraMix.App\Services\SpotifyApiService.cs",
    "NexoraMix.App\ViewModels\DeckViewModel.cs",
    "NexoraMix.App\ViewModels\MainViewModel.cs",
    "NexoraMix.App\Views\MainWindow.xaml",
    "NexoraMix.App\NexoraMix.App.csproj"
)

New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
foreach ($Relative in $Files) {
    $Source = Join-Path $Payload $Relative
    $Target = Join-Path $ProjectRoot $Relative
    if (-not (Test-Path $Source)) { throw "File hotfix mancante: $Source" }

    if (Test-Path $Target) {
        $Backup = Join-Path $BackupRoot $Relative
        New-Item -ItemType Directory -Force -Path (Split-Path $Backup -Parent) | Out-Null
        Copy-Item $Target $Backup -Force
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $Target -Parent) | Out-Null
    Copy-Item $Source $Target -Force
    Write-Host "Aggiornato: $Relative" -ForegroundColor Green
}

Get-ChildItem $ProjectRoot -Recurse -Directory -Force |
    Where-Object { $_.Name -in @("bin", "obj") } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Hotfix V5.3 applicato." -ForegroundColor Cyan
Write-Host "Backup: $BackupRoot"
Write-Host "Ora esegui:"
Write-Host "dotnet restore `"$Solution`""
Write-Host "dotnet build `"$Solution`" -c Debug"
