# Nexora Mix Studio V8 — Tablet Console Edition

Nexora Mix Studio V8 è una soluzione Visual Studio `.NET 8` composta da:

- applicazione DJ desktop WPF per Windows;
- motore audio locale a quattro deck;
- console remota touch servita dal PC tramite rete locale;
- wrapper Android nativo opzionale;
- interfaccia browser apribile anche sul monitor del PC;
- cartella Musica controllata automaticamente.

> L'app desktop è attualmente su `net10.0-windows`; Core e motore audio conservano i rispettivi target .NET 8. Lo SDK riproducibile è fissato in `global.json`.

## Nora DJ Copilot

La scheda **NORA AI** riconosce la traccia realmente udibile e propone Top 8 e un piano di tre tracce usando BPM, pitch, Camelot, energia, genere, anno, disponibilità del file, memoria recente e preferenze apprese.

Nora mostra spiegazione, confidenza, deck, cue di ingresso/uscita, durata del mix, bass swap e rischio. Può caricare una traccia solo su un deck completamente libero e può riprodurre una preview esclusivamente in cuffia; non avvia mai il master o il play al posto del DJ.

L'apprendimento e la memoria del set sono locali, persistenti, resettabili e separati per profilo/sessione. Il progetto compila senza dipendere dalle cartelle esterne `Nexora.AI` e `PuliziaSpazioDev`.

Documentazione: [architettura](docs/NORA_ARCHITECTURE.md), [analisi audio](docs/NORA_AUDIO_ANALYSIS.md), [ranking](docs/NORA_RANKING.md), [personalizzazione](docs/NORA_PERSONALIZATION.md), [planner](docs/NORA_SET_PLANNER.md), [transizioni](docs/NORA_TRANSITION_ADVISOR.md), [database](docs/NORA_DATABASE.md), [prestazioni](docs/NORA_PERFORMANCE.md), [deployment](docs/NORA_DEPLOYMENT.md) e [rapporto test](docs/NORA_TEST_REPORT.md).

## Architettura

Il PC resta l'unica sorgente audio e mantiene il clock dei quattro deck. Tablet e browser inviano comandi al PC e ricevono stato, waveform, BPM, posizione, meter, Sync ed effetti tramite WebSocket.

Questa scelta evita di duplicare il motore audio sul tablet e mantiene PC e tablet sincronizzati.

## Interfaccia Windows ricostruita

La finestra principale è divisa in sezioni chiare:

- **Console**: Deck A e B grandi con piatti, waveform e mixer centrale;
- **4 Deck**: A/B/C/D e mixer completo;
- **Libreria**: importazione, analisi BPM e caricamento deck;
- **Auto Mashup**: pianificazione 2–4 tracce;
- **Tablet / Android**: avvio server e codice di abbinamento;
- **Importa Musica**: cartella locale, Spotify e link YouTube aperto nel browser;
- **Controller MIDI**: collegamento controller generici.

I controlli essenziali sono sempre visibili:

- Play/Pausa;
- Cue e Set Cue;
- Sync e Master;
- Stop;
- BPM manuale;
- pitch/tempo;
- hot cue 1–8;
- loop 1/2/4/8/16/32 battute;
- EQ Low/Mid/High;
- filtro;
- Echo, Crush, Saturation, Gate, Compressor, Roll e Brake;
- channel fader e crossfader.

## Console tablet

La console touch include:

- piatti animati in tempo reale quando il deck è in riproduzione;
- movimento del piatto tramite touch per nudge/seek;
- waveform con playhead;
- BPM originale ed effettivo;
- regolazione BPM e pitch;
- Play, Cue, Set Cue, Sync, Master e Stop;
- loop quantizzati;
- 8 hot cue;
- effetti;
- mixer 4 canali;
- crossfader;
- registrazione e Auto Mashup;
- modalità 2 Deck, 4 Deck e Mixer.

### Avvio dal browser Android

1. Avviare Nexora Mix Studio sul PC.
2. Aprire la scheda **Tablet / Android**.
3. Premere **AVVIA CONSOLE TABLET**.
4. Collegare PC e tablet alla stessa rete Wi-Fi.
5. Aprire sul tablet l'indirizzo mostrato dal programma.
6. Inserire il codice a sei cifre.

La stessa pagina può essere aperta sul PC con il pulsante **APRI SU QUESTO PC**, ottenendo una vista replicata sul monitor.

### App Android nativa

Aprire separatamente:

```text
NexoraMix.Tablet.Android.sln
```

Requisiti:

- Visual Studio;
- workload **Sviluppo di app mobili con .NET**;
- Android SDK;
- .NET 8 Android workload.

Build:

```powershell
.\Build-Tablet-Android.ps1 -Configuration Debug
```

L'app Android è un wrapper WebView dedicato alla console remota e conserva l'ultimo indirizzo del PC.

## Cartella Musica

Il software crea:

```text
%LOCALAPPDATA%\NexoraMix\Music\Inbox
```

Quando viene copiato un file audio supportato nella cartella `Inbox`, Nexora lo rileva e lo importa automaticamente.

Formati gestiti in base ai decoder disponibili su Windows:

```text
MP3
WAV
FLAC
AIFF
AAC
M4A
WMA
```

## YouTube

Nexora non include automazioni verso siti di conversione YouTube→MP3 e non estrae il flusso audio di YouTube. Il comando **Incolla e apri link YouTube** apre il contenuto nel browser; per il mixer occorre poi importare un file locale che l'utente possiede o è autorizzato a usare.

Non sono inclusi `yt5s`, TurboScribe downloader, stream ripping o aggiramento di protezioni.

## Compilazione Windows

Requisiti:

- Windows 10/11 x64;
- Visual Studio con workload **Sviluppo desktop .NET**;
- .NET 8 SDK;
- dispositivo audio Windows.

Aprire:

```text
NexoraMixStudio.sln
```

Impostare `NexoraMix.App` come progetto di avvio.

Da PowerShell:

```powershell
.\Build.ps1 -Configuration Debug
.\Run.ps1
```

## Rete e firewall

Il server tablet ascolta per impostazione predefinita sulla porta:

```text
17840/TCP
```

Alla prima esecuzione Windows può chiedere di autorizzare l'app nel firewall. Consentire l'accesso soltanto sulle reti private fidate.

## Limiti dichiarati

- Il touch del piatto offre nudge/seek remoto; non sostituisce un jog HID professionale a bassissima latenza.
- Il PC deve restare acceso e l'app Windows deve essere in esecuzione.
- La qualità della console tablet dipende dalla rete Wi-Fi locale.
- Il wrapper Android richiede compilazione su Windows con il workload Android.
- Il progetto è stato sottoposto a validazione statica; build e test reali devono essere eseguiti su Windows.
