# Nexora Mix Studio V8 — Agent Guide

## Leader

`00-leader` coordina le fasi, impedisce modifiche concorrenti, richiede build/test e aggiorna `docs/WORKLOG.md`.

## Aree principali

- Desktop WPF: `NexoraMix.App`;
- motore audio: `NexoraMix.Audio`;
- modelli e servizi: `NexoraMix.Core`;
- console web/tablet: `NexoraMix.App/RemoteUi`;
- wrapper Android: `NexoraMix.Tablet.Android`;
- test: `NexoraMix.SelfTest` e `NexoraMix.ControllerTests`.

## Regole

- Modificare massimo 4–6 file per blocco.
- Eseguire `Validate-Project.ps1` e `Build.ps1` dopo ogni blocco.
- Non inserire downloader YouTube, stream ripping o aggiramento DRM.
- Proprietà readonly WPF sempre con binding `OneWay`.
- Il PC resta la sorgente del clock audio; il tablet è una superficie di controllo.
- Non dichiarare compatibilità controller o Android senza build/test reale.
- Non eseguire commit o push senza richiesta.
- Aggiornare `docs/WORKLOG.md` e il rapporto dell'agente.

## Comandi

```powershell
.\Validate-Project.ps1
.\Build.ps1 -Configuration Debug
.\Build.ps1 -Configuration Release
.\Build-Tablet-Android.ps1 -Configuration Debug
```
