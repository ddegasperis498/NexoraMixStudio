# Architettura V8

## Componenti

```text
NexoraMix.App (WPF)
  ├─ MainWindow / DeckPanel / MixerPanel
  ├─ MainViewModel
  ├─ TabletRemoteHost (ASP.NET Core)
  └─ RemoteUi (HTML/CSS/JS)

NexoraMix.Audio
  ├─ MasterAudioEngine
  ├─ DeckChannel A/B/C/D
  ├─ DSP / EQ / FX
  ├─ BPM analysis
  └─ Generic MIDI

NexoraMix.Core
  ├─ modelli
  ├─ sync math
  ├─ AutoMix / AutoMashup planner
  └─ sessioni

NexoraMix.Tablet.Android
  └─ WebView wrapper verso il server del PC
```

## Principio fondamentale

Il PC è la sorgente autorevole di audio, posizione, BPM, Sync e stato. Il tablet non esegue il mix: invia comandi e visualizza snapshot ricevuti dal PC.

## Comunicazione

- HTTP per shell e stato iniziale;
- WebSocket per snapshot e comandi;
- pairing code temporaneo;
- aggiornamento stato circa ogni 100 ms;
- interpolazione grafica locale dei piatti fra gli snapshot.

## Cartella musica

`MainViewModel.MediaImport` crea e monitora `%LOCALAPPDATA%\NexoraMix\Music\Inbox`.
