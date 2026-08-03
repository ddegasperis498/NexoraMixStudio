# Deployment Nora

## Requisiti

- Windows 10/11 x64;
- .NET SDK 10.0.302, fissato in `global.json`;
- workload desktop Windows per sviluppo;
- dispositivo audio compatibile con Windows per uso live.

## Verifica locale

```powershell
.\Validate-Project.ps1
.\Build.ps1 -Configuration Debug
.\Build.ps1 -Configuration Release
```

`Build.ps1` interrompe immediatamente l'esecuzione quando validazione, restore, build o una suite restituiscono un codice di errore.

## Pubblicazione Windows

```powershell
dotnet publish .\NexoraMix.App\NexoraMix.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=false -o .\.artifacts\publish\win-x64
```

La pubblicazione è framework-dependent: sul PC destinazione deve essere installato il runtime desktop .NET corrispondente.

## CI

Il workflow Windows esegue scan dei riferimenti esterni e dei percorsi personali, restore, validazione, Debug, Release, test, publish, ZIP e upload di log/artifact. Il repository non richiede le cartelle sorelle presenti sul computer di sviluppo.

## Dati

I database personali restano in `%LOCALAPPDATA%` e non sono inclusi nell'artefatto. Eseguire un backup prima di migrare un catalogo importante; le modifiche di Nora al catalogo sono additive.
