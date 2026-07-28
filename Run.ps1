$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project .\NexoraMix.App\NexoraMix.App.csproj
}
finally { Pop-Location }
