param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipSelfTest,
    [switch]$SkipClean
)

$ErrorActionPreference = 'Stop'

function Invoke-CheckedStep {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [scriptblock]$Action
    )

    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Fase '$Name' fallita con exit code $LASTEXITCODE."
    }
}

Push-Location $PSScriptRoot
try {
    Invoke-CheckedStep 'Validate-Project' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\Validate-Project.ps1 -Root $PSScriptRoot
    }

    if (-not $SkipClean) {
        Invoke-CheckedStep 'dotnet clean' { & dotnet clean .\NexoraMixStudio.sln -c $Configuration }
    }

    Invoke-CheckedStep 'dotnet restore' { & dotnet restore .\NexoraMixStudio.sln }
    Invoke-CheckedStep 'dotnet build' { & dotnet build .\NexoraMixStudio.sln -c $Configuration --no-restore }

    if (-not $SkipSelfTest) {
        Invoke-CheckedStep 'NexoraMix.SelfTest' {
            & dotnet run --project .\NexoraMix.SelfTest\NexoraMix.SelfTest.csproj -c $Configuration --no-build
        }
        Invoke-CheckedStep 'NexoraMix.ControllerTests' {
            & dotnet run --project .\NexoraMix.ControllerTests\NexoraMix.ControllerTests.csproj -c $Configuration --no-build
        }
    }

    Write-Host "Build e validazione completate: $Configuration" -ForegroundColor Green
}
finally {
    Pop-Location
}
