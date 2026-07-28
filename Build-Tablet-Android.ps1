param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "NexoraMix.Tablet.Android\NexoraMix.Tablet.Android.csproj"

dotnet workload restore $project
dotnet restore $project
dotnet build $project -c $Configuration

Write-Host "Build Android completata. Cerca l'APK sotto NexoraMix.Tablet.Android\bin\$Configuration\net8.0-android\" -ForegroundColor Green
