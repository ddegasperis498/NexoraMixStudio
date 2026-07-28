param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipSelfTest,
    [switch]$SkipClean
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & .\Validate-Project.ps1 -Root $PSScriptRoot

    if (-not $SkipClean) {
        dotnet clean .\NexoraMixStudio.sln -c $Configuration
    }

    dotnet restore .\NexoraMixStudio.sln
    dotnet build .\NexoraMixStudio.sln -c $Configuration --no-restore

    if (-not $SkipSelfTest) {
        dotnet run --project .\NexoraMix.SelfTest\NexoraMix.SelfTest.csproj -c $Configuration --no-build
        dotnet run --project .\NexoraMix.ControllerTests\NexoraMix.ControllerTests.csproj -c $Configuration --no-build
    }

    Write-Host "Build e validazione completate: $Configuration" -ForegroundColor Green
}
finally {
    Pop-Location
}
