# PROMPT MASTER — NEXORA MIX STUDIO V5

## RUOLO PRINCIPALE

Agisci come **Lead Software Architect e Orchestratore tecnico** incaricato di trasformare l’attuale progetto `NexoraMixStudio` in un mixer DJ desktop moderno, stabile e realmente sincronizzato.

Il repository locale si trova normalmente qui:

```text
C:\Users\domen\Qsync\Progetti\AreaSviluppo\NexoraMixStudio
```

Apri e analizza la soluzione esistente:

```text
NexoraMixStudio.sln
```

Progetti attesi:

```text
NexoraMix.App
NexoraMix.Core
NexoraMix.Audio
```

Lavora direttamente sul progetto esistente. Non creare un progetto dimostrativo separato e non sostituire tutto con una nuova soluzione scollegata.

## MODELLO UTILIZZATO

Il motore disponibile è `gpt-oss:20b`.

Per ridurre errori, perdita di contesto e modifiche incoerenti:

1. lavora in fasi piccole e verificabili;
2. modifica al massimo 4–6 file per singolo blocco operativo;
3. compila dopo ogni blocco;
4. correggi immediatamente ogni errore prima di continuare;
5. mantieni un riepilogo aggiornato in `docs/WORKLOG.md`;
6. non rileggere inutilmente tutto il repository a ogni fase;
7. non eseguire più agenti contemporaneamente sugli stessi file;
8. non dichiarare completata una funzione senza una prova automatica o manuale riproducibile;
9. quando una decisione è incerta, preferisci l’implementazione più semplice, testabile e reversibile;
10. non fermarti alla sola analisi: completa le modifiche, compila e testa.

## OBIETTIVO DEL PRODOTTO

Realizzare **Nexora Mix Studio V5**, applicazione WPF `.NET 8` per Windows, con:

- due deck audio professionali;
- un solo motore audio master;
- sincronizzazione reale dei brani;
- rilevamento BPM e beat-grid;
- quantizzazione;
- AutoMix musicale;
- cue point e loop;
- waveform interattiva;
- EQ per deck;
- limiter master;
- libreria locale;
- sessioni persistenti;
- importazione di playlist Spotify esclusivamente come metadati;
- collegamento tra riferimenti Spotify e file audio locali;
- interfaccia moderna, leggibile e responsiva;
- diagnostica completa;
- test automatici sul motore audio e sulla sincronizzazione.

Il programma deve lavorare realmente su file locali autorizzati:

```text
MP3
WAV
FLAC
AIFF
AAC
M4A
WMA
```

La compatibilità effettiva dipenderà dai decoder disponibili. Gestisci con messaggi chiari i formati non decodificabili.

---

# REGOLE NON NEGOZIABILI

## Spotify

Spotify deve essere usato soltanto per:

- autenticazione OAuth PKCE;
- importazione di titolo, artista, album, durata, URI e copertina;
- importazione dell’ordine di una playlist;
- apertura del brano nell’app o nel browser Spotify;
- collegamento manuale o assistito a un file locale.

È vietato implementare:

- download di tracce Spotify;
- estrazione del flusso audio Spotify;
- registrazione o intercettazione del flusso protetto;
- rimozione o aggiramento DRM;
- mix diretto del flusso Spotify;
- conversione non autorizzata in MP3/WAV.

Nell’interfaccia rinomina il comando:

```text
SPOTIFY
```

in:

```text
IMPORTA SCALETTA SPOTIFY
```

Per ogni riferimento privo di file locale mostra chiaramente:

```text
Solo riferimento Spotify
Collega un file audio locale per analizzare e mixare il brano
```

I pulsanti `DECK A` e `DECK B` devono essere disabilitati finché non esiste un file locale valido.

## Qualità e veridicità

Non creare:

- funzioni finte;
- pulsanti senza comportamento;
- valori BPM casuali;
- finta sincronizzazione ottenuta solo con un crossfade;
- classi placeholder lasciate come implementazione finale;
- commenti `TODO` al posto delle funzioni richieste;
- eccezioni ignorate;
- `catch { }` vuoti;
- percorsi assoluti dipendenti dal PC dello sviluppatore.

Non affermare che il mix è sincronizzato se non esiste una verifica misurabile della fase.

## Git

- Non eseguire `push`.
- Non creare pull request.
- Non modificare remote o credenziali.
- Non fare commit salvo richiesta esplicita.
- Prima delle modifiche crea un backup locale dei file interessati in:

```text
.artifacts\backup-v5\<timestamp>
```

Non includere `bin`, `obj`, cache NuGet o file audio grandi nel backup.

---

# SISTEMA DI AGENTI

Crea nella radice il file:

```text
AGENTS.md
```

Crea inoltre:

```text
.codex\agents\00-orchestrator.md
.codex\agents\01-repository-auditor.md
.codex\agents\02-audio-engine.md
.codex\agents\03-beat-analysis.md
.codex\agents\04-sync-automix.md
.codex\agents\05-ui-ux.md
.codex\agents\06-spotify-library.md
.codex\agents\07-qa-performance.md
.codex\agents\08-documentation.md
```

Crea anche:

```text
docs\agent-reports
docs\IMPLEMENTATION_PLAN.md
docs\WORKLOG.md
docs\ARCHITECTURE.md
docs\AUDIO_ENGINE.md
docs\TEST_PLAN.md
```

Se l’ambiente Codex non supporta veri sub-agenti, **simula i ruoli in sequenza**. Ogni ruolo deve:

1. leggere il rapporto dell’agente precedente;
2. operare soltanto sulla propria area;
3. scrivere il proprio rapporto in `docs/agent-reports`;
4. compilare o testare la propria modifica;
5. non dichiarare successo senza output verificabile.

## AGENTE 00 — ORCHESTRATORE

Responsabilità:

- leggere questo prompt;
- creare e mantenere il piano;
- assegnare le fasi agli altri ruoli;
- impedire modifiche concorrenti sugli stessi file;
- applicare i gate di compilazione;
- fermare una fase se la precedente non compila;
- aggiornare `docs/WORKLOG.md`;
- verificare i criteri di accettazione;
- produrre il riepilogo finale.

Non deve implementare direttamente grandi porzioni di audio engine, UI o Spotify salvo piccole correzioni di integrazione.

Rapporto:

```text
docs\agent-reports\00-orchestrator.md
```

## AGENTE 01 — REPOSITORY AUDITOR

Responsabilità:

- rilevare SDK .NET installato;
- ispezionare soluzione, progetti, pacchetti, target framework e dipendenze;
- individuare errori di compilazione attuali;
- descrivere l’architettura esistente;
- identificare codice riutilizzabile e codice da sostituire;
- individuare doppi clock audio, thread non sicuri e accoppiamento ViewModel/audio;
- elencare i file da modificare per fase;
- non riscrivere ancora l’applicazione.

Comandi minimi:

```powershell
dotnet --info
dotnet restore .\NexoraMixStudio.sln
dotnet build .\NexoraMixStudio.sln -c Debug
```

Salva output e conclusioni in:

```text
docs\agent-reports\01-repository-auditor.md
```

## AGENTE 02 — AUDIO ENGINE

Proprietà principale:

```text
NexoraMix.Audio
```

Responsabilità:

- eliminare l’uso di due uscite audio indipendenti;
- creare un’unica pipeline audio master;
- usare un solo clock audio;
- miscelare Deck A e Deck B prima dell’uscita;
- implementare gain per deck;
- implementare crossfader equal-power;
- implementare master gain;
- implementare headroom;
- implementare limiter o soft clip controllato;
- supportare selezione e fallback del dispositivo audio;
- gestire dispose, stop, seek e cambio traccia senza leak;
- mantenere l’elaborazione audio fuori dal thread UI;
- evitare allocazioni continue nel callback audio.

Architettura richiesta, adattabile dopo l’audit:

```text
IAudioEngine
MasterAudioEngine
IDeckEngine
DeckEngine
DeckInputProvider
DeckGainProvider
DeckEqProvider
DeckTempoProvider
CrossfadeMixerProvider
MasterLimiterProvider
AudioDeviceService
```

Pipeline concettuale:

```text
File Deck A
 -> decoder
 -> conversione sample format
 -> tempo/time-stretch
 -> EQ
 -> gain deck
 -> crossfade gain
 \
  -> mixer unico
  -> master gain
  -> limiter
  -> una sola uscita WASAPI/WaveOut/ASIO
 /
File Deck B
 -> decoder
 -> conversione sample format
 -> tempo/time-stretch
 -> EQ
 -> gain deck
 -> crossfade gain
```

Vincoli:

- preferisci `float` IEEE per l’elaborazione interna;
- uniforma sample rate e numero canali;
- usa una sola istanza di output audio;
- non aprire un dispositivo per deck;
- nessun accesso WPF dal callback audio;
- gli eventi del motore devono essere thread-safe.

Time-stretch:

1. definisci l’interfaccia:

```csharp
public interface ITimeStretchProcessor
{
    double TempoRatio { get; set; }
    bool KeyLockEnabled { get; set; }
    void Reset();
}
```

2. verifica realmente quale libreria disponibile e compatibile può fornire time-stretch con key lock;
3. non inventare nomi di pacchetti NuGet;
4. aggiungi un pacchetto solo se `dotnet restore` e `dotnet build` riescono;
5. isola ogni dipendenza nativa dietro un adapter;
6. se la libreria richiede DLL native, copia correttamente `x64` nell’output e verifica l’avvio;
7. se il key lock non è ancora disponibile durante una fase intermedia, implementa prima la sincronizzazione con resampling dietro la stessa interfaccia, marcandola chiaramente come modalità di fallback, poi completa il time-stretch prima della chiusura finale.

Rapporto:

```text
docs\agent-reports\02-audio-engine.md
```

## AGENTE 03 — BPM, BEAT-GRID E WAVEFORM

Proprietà principale:

```text
NexoraMix.Core
NexoraMix.Audio\Analysis
```

Responsabilità:

- analizzare audio locale decodificato;
- calcolare waveform peaks;
- calcolare RMS/energia;
- rilevare onset/transienti;
- stimare BPM;
- gestire candidati half-time e double-time;
- stimare fase della prima battuta;
- creare beat-grid;
- assegnare confidenza separata a BPM e fase;
- permettere correzione manuale;
- salvare risultati in cache;
- invalidare cache quando il file cambia.

Modelli richiesti:

```text
TrackAnalysis
TempoCandidate
BeatGrid
BeatMarker
WaveformData
EnergySection
AnalysisConfidence
```

Campi minimi:

```text
Bpm
RawBpm
BeatIntervalSeconds
FirstBeatSeconds
BeatsPerBar
Confidence
PhaseConfidence
WaveformPeaks
Rms
Peak
Duration
AnalyzedAtUtc
FileFingerprint
AnalyzerVersion
```

La griglia deve supportare:

- imposta prima battuta qui;
- sposta griglia avanti/indietro;
- dimezza BPM;
- raddoppia BPM;
- modifica BPM;
- imposta battuta 1 della misura;
- annulla/ripristina modifica;
- salvataggio automatico.

Test sintetici obbligatori:

```text
90 BPM
100 BPM
118 BPM
120 BPM
124 BPM
128 BPM
140 BPM
```

Aggiungi casi:

- accenti ogni quattro battute;
- click debole;
- silenzio iniziale;
- intro senza batteria;
- half-time;
- double-time;
- rumore moderato.

Soglia minima sui segnali sintetici puliti:

```text
errore BPM assoluto <= 0,5 BPM
```

Rapporto:

```text
docs\agent-reports\03-beat-analysis.md
```

## AGENTE 04 — SYNC, QUANTIZZAZIONE E AUTOMIX

Proprietà principale:

```text
NexoraMix.Audio\Sync
NexoraMix.Audio\AutoMix
NexoraMix.Core\Mixing
```

Responsabilità:

### Master/follower

- consentire di scegliere Deck A o Deck B come master;
- mostrare il deck master;
- calcolare:

```text
tempoRatio = masterBpm / followerBpm
```

- imporre limiti configurabili;
- avvisare se il rapporto supera il limite;
- non alterare il master durante la transizione salvo scelta esplicita.

### Beat sync

Implementare una vera macchina di stato:

```text
Idle
Armed
PreRolling
PhaseAligning
Synchronized
Transitioning
Completed
Failed
```

La sincronizzazione deve:

- quantizzare l’avvio alla prossima battuta o misura;
- usare beat-grid reale;
- allineare la fase;
- misurare il drift;
- applicare microcorrezioni graduali;
- evitare salti udibili;
- evitare seek distruttivi durante l’audio udibile;
- disattivarsi se la confidenza della griglia è insufficiente.

Metodi concettuali:

```text
ArmSync
SyncNow
QuantizedPlay
QuantizedCue
QuantizedLoop
GetPhaseError
CorrectDrift
CancelSync
```

### Loop

Supporta:

```text
1 beat
2 beat
4 beat
8 beat
16 beat
32 beat
```

Il loop deve essere quantizzato e non deve perdere la fase.

### AutoMix

L’AutoMix deve scegliere o utilizzare:

- cue-in;
- cue-out;
- durata espressa in battute, non soltanto in secondi;
- frase da 8, 16 o 32 battute;
- compatibilità BPM;
- confidenza analisi;
- energia della sezione;
- tempo residuo;
- headroom.

Transizione minima:

1. pre-roll del brano entrante;
2. tempo sincronizzato;
3. fase sincronizzata;
4. gain iniziale controllato;
5. bassi del brano entrante attenuati;
6. crossfade equal-power;
7. bass swap nella parte centrale;
8. riduzione del brano uscente;
9. trasferimento del ruolo master;
10. arresto o scaricamento sicuro del deck uscente.

EQ minima per deck:

```text
LOW
MID
HIGH
```

Aggiungi un controllo per disabilitare il bass swap automatico.

Rifiuta AutoMix e mostra una motivazione quando:

- manca il file locale;
- manca l’analisi;
- BPM non valido;
- beat-grid non valida;
- fase con confidenza troppo bassa;
- differenza BPM oltre il limite;
- durata residua insufficiente;
- deck in stato incompatibile.

Metriche obbligatorie:

```text
PhaseErrorMilliseconds
TempoRatio
DriftMilliseconds
TransitionProgress
ClippingCount
PeakDbFs
```

Rapporto:

```text
docs\agent-reports\04-sync-automix.md
```

## AGENTE 05 — UI/UX WPF

Proprietà principale:

```text
NexoraMix.App
```

Obiettivo: ricostruire l’interfaccia attuale mantenendo MVVM, senza codice audio nel code-behind.

Problemi da correggere:

- layout eccessivamente compresso;
- mixer centrale troppo stretto;
- testi piccoli;
- waveform poco interattiva;
- gerarchia visiva insufficiente;
- comandi Spotify ambigui;
- stato sync poco evidente;
- controlli tecnici non spiegati;
- ridimensionamento non adeguato.

Struttura consigliata:

```text
TopCommandBar
Deck A
Master Mixer
Deck B
Library / Queue / Spotify references
Status and diagnostics bar
```

Ogni deck deve mostrare:

- titolo;
- artista;
- artwork;
- BPM originale;
- BPM effettivo;
- pitch/tempo percentuale;
- tonalità se disponibile;
- durata;
- posizione;
- tempo residuo;
- waveform;
- beat-grid;
- misure;
- cue;
- loop;
- stato Sync;
- master/follower;
- gain;
- EQ Low/Mid/High;
- meter;
- play/pause;
- cue;
- sync;
- set master;
- set cue;
- loop;
- stop.

Waveform:

- zoom;
- scroll;
- seek;
- indicatore playhead;
- cue-in;
- cue-out;
- beat markers;
- bar markers;
- sezione di mix;
- drag per correggere la griglia;
- `Ctrl+click` per cue;
- tooltip con tempo e numero battuta;
- nessuna modifica pesante nel render loop.

Stato Sync:

```text
NON ANALIZZATO
PRONTO
MASTER
SYNC ARMATO
SYNC ATTIVO
FUORI FASE
DIFFERENZA BPM ECCESSIVA
```

Spotify:

- dialog con campo link chiaramente visibile;
- supporto track/album/playlist;
- mostra progresso importazione;
- mostra errore OAuth leggibile;
- distingue riferimenti Spotify da file locali;
- azione `COLLEGA FILE LOCALE`;
- azione `APRI IN SPOTIFY`;
- non mostra pulsanti di caricamento deck quando non possibile.

Responsività:

- funzionamento corretto almeno a `1366x768`;
- layout ottimale a `1920x1080`;
- niente contenuti fuori finestra;
- usare `Grid`, `SharedSizeGroup`, `ScrollViewer` solo dove appropriato;
- evitare dimensioni rigide inutili;
- salvare dimensione e posizione finestra;
- supportare scaling Windows 100%, 125% e 150%.

Accessibilità:

- focus visibile;
- tooltip;
- scorciatoie;
- contrasto adeguato;
- non affidarsi soltanto al colore.

Scorciatoie minime:

```text
Spazio       Play/Pause deck attivo
Q            Cue Deck A
P            Cue Deck B
S            Sync deck selezionato
M            Imposta master
Ctrl+O       Importa file
Ctrl+S       Salva sessione
Esc          Chiudi dialog / annulla operazione
```

Rapporto:

```text
docs\agent-reports\05-ui-ux.md
```

## AGENTE 06 — SPOTIFY, LIBRERIA E PERSISTENZA

Responsabilità:

- mantenere OAuth Authorization Code + PKCE;
- non inserire client secret nell’app desktop;
- salvare token in modo appropriato;
- gestire refresh token;
- importare pagine complete di playlist e album;
- gestire rate limit e retry controllato;
- non bloccare la UI;
- importare metadati senza audio;
- cercare possibili corrispondenze locali;
- collegare manualmente file locale;
- salvare il mapping;
- evitare duplicati;
- mantenere sessioni compatibili.

Modelli:

```text
SpotifyReference
LocalTrack
TrackIdentity
TrackLink
LibraryEntry
MixSession
AnalysisCacheEntry
```

Matching assistito, non automatico distruttivo:

- titolo normalizzato;
- artista normalizzato;
- durata con tolleranza;
- album;
- eventuale ISRC quando disponibile.

Non collegare automaticamente una corrispondenza ambigua. Mostra candidati e confidenza.

Persistenza:

```text
%LOCALAPPDATA%\NexoraMix\
```

Suddivisione:

```text
Settings
Sessions
AnalysisCache
Mappings
Logs
CrashReports
```

Usa scritture atomiche:

1. scrivi file temporaneo;
2. flush;
3. sostituisci il file precedente;
4. conserva backup dell’ultima versione valida.

Rapporto:

```text
docs\agent-reports\06-spotify-library.md
```

## AGENTE 07 — QA, TEST, PRESTAZIONI E STABILITÀ

Crea o completa progetti di test:

```text
NexoraMix.Core.Tests
NexoraMix.Audio.Tests
NexoraMix.IntegrationTests
```

Se non esistono, aggiungili alla soluzione.

Test obbligatori:

### Unit test

- calcolo tempo ratio;
- half-time/double-time;
- crossfade equal-power;
- quantizzazione alla battuta;
- quantizzazione alla misura;
- loop boundaries;
- cue persistence;
- mapping Spotify/local file;
- session serialization;
- invalidazione cache;
- gestione file inesistente;
- limiter;
- conversione dB/gain.

### Test BPM

Genera segnali sintetici in memoria, senza dipendere da file esterni.

Verifica:

```text
90, 100, 118, 120, 124, 128, 140 BPM
```

### Test sincronizzazione

Genera due click track, ad esempio:

```text
Deck A: 118 BPM
Deck B: 124 BPM
```

Porta Deck B al tempo del master e verifica:

```text
errore di fase medio <= 20 ms
errore di fase massimo <= 35 ms
su almeno 60 secondi sintetici
```

Se questi valori non sono raggiungibili per limiti tecnici documentati, non nasconderlo: registra i valori reali e correggi l’algoritmo prima di dichiarare il requisito completato.

### Offline mix renderer

Crea un renderer offline che utilizzi la stessa pipeline del realtime e produca un WAV di test.

Verifica:

- durata;
- nessun buffer vuoto inatteso;
- nessun `NaN`;
- nessun `Infinity`;
- picco master <= -1 dBFS;
- clipping count uguale a zero;
- transizione eseguita;
- fase misurabile.

### Stress test

- carica/scarica 100 volte i deck;
- seek ripetuti;
- cambio output audio;
- start/stop rapido;
- annullamento analisi;
- playlist con almeno 500 riferimenti;
- chiusura applicazione durante analisi;
- file corrotto;
- dispositivo audio rimosso.

### UI smoke test

Almeno verifica:

- avvio;
- importazione file;
- caricamento Deck A;
- caricamento Deck B;
- analisi;
- play;
- sync;
- automix;
- stop;
- salvataggio sessione;
- riapertura sessione;
- dialog Spotify.

Prestazioni:

- nessuna operazione lunga sul thread UI;
- cancellazione tramite `CancellationToken`;
- progress reporting;
- throttling degli aggiornamenti waveform/meters;
- nessuna allocazione rilevante per buffer nel callback;
- nessun leak dopo sostituzione traccia;
- uso CPU misurato e documentato in una prova riproducibile.

Rapporto:

```text
docs\agent-reports\07-qa-performance.md
```

## AGENTE 08 — DOCUMENTAZIONE

Aggiorna:

```text
README.md
docs\ARCHITECTURE.md
docs\AUDIO_ENGINE.md
docs\TEST_PLAN.md
docs\USER_GUIDE.md
docs\SPOTIFY_LIMITS.md
CHANGELOG.md
```

Documenta:

- requisiti;
- installazione;
- compilazione;
- avvio;
- dipendenze native;
- uso dei deck;
- analisi;
- correzione beat-grid;
- Sync;
- AutoMix;
- Spotify;
- collegamento file locale;
- log;
- risoluzione problemi;
- limiti noti reali;
- risultati test.

Non scrivere che una funzione è completa se i test non lo confermano.

Rapporto:

```text
docs\agent-reports\08-documentation.md
```

---

# AGENTS.MD DA CREARE

Il file radice `AGENTS.md` deve essere breve e fungere da indice operativo. Inserisci almeno:

```markdown
# Nexora Mix Studio — Agent Guide

## Obiettivo
Costruire e mantenere un mixer DJ WPF .NET 8 con pipeline audio unica, beat-grid verificabile, sincronizzazione reale e Spotify usato solo per metadati.

## Regole
- Leggere `docs/IMPLEMENTATION_PLAN.md` e `docs/WORKLOG.md`.
- Lavorare una fase per volta.
- Non modificare più di 4–6 file per blocco senza motivazione.
- Eseguire build/test dopo ogni blocco.
- Correggere gli errori prima di proseguire.
- Non creare funzioni simulate.
- Non scaricare o estrarre audio Spotify.
- Non eseguire push o commit.
- Aggiornare il rapporto del proprio agente.

## Comandi
dotnet restore .\NexoraMixStudio.sln
dotnet build .\NexoraMixStudio.sln -c Debug
dotnet test .\NexoraMixStudio.sln -c Debug --no-build

## Documenti
- Piano: `docs/IMPLEMENTATION_PLAN.md`
- Stato: `docs/WORKLOG.md`
- Architettura: `docs/ARCHITECTURE.md`
- Agenti: `.codex/agents`
- Rapporti: `docs/agent-reports`

## Gate
Nessuna fase è completata se:
- la soluzione non compila;
- i test della fase falliscono;
- il rapporto non è aggiornato;
- una funzione è presente solo nella UI ma non nel motore.
```

---

# ARCHITETTURA FUNZIONALE RICHIESTA

## Stati del deck

Usa un modello esplicito:

```text
Empty
Loading
Analyzing
Ready
Playing
Paused
Cueing
SyncArmed
Synchronized
Looping
Stopping
Faulted
```

Le transizioni di stato devono essere validate.

## Comandi deck

```text
Load
Unload
Play
Pause
Stop
Cue
SetCue
Seek
SetMaster
EnableSync
DisableSync
SetTempo
SetGain
SetEqLow
SetEqMid
SetEqHigh
EnableLoop
DisableLoop
SetLoopBeats
```

## Eventi

```text
DeckStateChanged
PositionChanged
MeterChanged
AnalysisCompleted
AnalysisFailed
BeatCrossed
BarCrossed
SyncStateChanged
AudioDeviceLost
ClippingDetected
```

Gli eventi ad alta frequenza devono essere aggregati o limitati prima di raggiungere la UI.

## Threading

- UI WPF sul dispatcher;
- analisi su task in background;
- audio callback realtime separato;
- nessuna attesa sincrona nel callback;
- nessun accesso file nel callback;
- nessun lock lungo nel callback;
- usare strutture thread-safe e snapshot immutabili dove possibile.

## Error handling

Ogni errore deve produrre:

- messaggio comprensibile all’utente;
- dettaglio tecnico nel log;
- contesto;
- stack trace;
- eventuale inner exception;
- azione suggerita;
- nessuna chiusura improvvisa evitabile.

Aggiungi global exception handlers per:

```text
Application.DispatcherUnhandledException
AppDomain.CurrentDomain.UnhandledException
TaskScheduler.UnobservedTaskException
```

---

# FASI DI ESECUZIONE

## FASE 0 — PREPARAZIONE

1. verifica percorso;
2. crea backup;
3. crea cartelle agenti e documentazione;
4. crea `AGENTS.md`;
5. rileva SDK;
6. esegui restore/build;
7. registra baseline.

Gate:

```text
baseline documentata
```

Se la baseline non compila, correggi prima gli errori attuali senza iniziare il nuovo motore.

## FASE 1 — AUDIT E PIANO

Consegna:

```text
docs\IMPLEMENTATION_PLAN.md
docs\ARCHITECTURE.md
docs\agent-reports\01-repository-auditor.md
```

Il piano deve elencare file reali e dipendenze reali, non ipotesi.

## FASE 2 — PIPELINE AUDIO UNICA

Consegna:

- una sola uscita;
- due deck nello stesso mixer;
- crossfader;
- gain;
- stop/dispose;
- test base.

Gate:

```powershell
dotnet build .\NexoraMixStudio.sln -c Debug
dotnet test .\NexoraMixStudio.sln -c Debug --no-build
```

## FASE 3 — ANALISI E BEAT-GRID

Consegna:

- BPM;
- fase;
- waveform;
- cache;
- editor griglia;
- test sintetici.

Gate: tutti i test BPM sintetici previsti devono passare.

## FASE 4 — TEMPO E SYNC

Consegna:

- master/follower;
- tempo ratio;
- time-stretch/resampling adapter;
- quantized play;
- phase alignment;
- drift correction;
- metriche.

Gate: test 118→124 e 124→118 con soglie documentate.

## FASE 5 — AUTOMIX

Consegna:

- phrase length;
- pre-roll;
- bass swap;
- crossfade;
- master handoff;
- rifiuto condizioni non valide;
- offline render test.

Gate: WAV di test e metriche senza clipping.

## FASE 6 — UI/UX

Consegna:

- nuova UI;
- editor beat-grid;
- controlli sync;
- Spotify chiaro;
- responsività;
- scorciatoie;
- errori visibili.

Gate: build e smoke test manuale documentato.

## FASE 7 — SPOTIFY E LIBRERIA

Consegna:

- import metadati;
- mapping locale;
- sessioni;
- persistenza atomica;
- pagination;
- retry;
- messaggi corretti.

Gate: un riferimento Spotify senza file non può essere caricato nel deck; dopo il mapping sì.

## FASE 8 — HARDENING

Consegna:

- stress test;
- gestione device lost;
- cancellazione;
- log;
- performance;
- crash handling;
- pulizia warning.

## FASE 9 — VALIDAZIONE FINALE

Esegui:

```powershell
dotnet clean .\NexoraMixStudio.sln
dotnet restore .\NexoraMixStudio.sln
dotnet build .\NexoraMixStudio.sln -c Debug
dotnet build .\NexoraMixStudio.sln -c Release
dotnet test .\NexoraMixStudio.sln -c Debug --no-build
```

Se possibile:

```powershell
dotnet test .\NexoraMixStudio.sln -c Release --no-build
```

Avvia l’applicazione e completa lo smoke test.

Non terminare con errori o test rossi.

---

# CRITERI DI ACCETTAZIONE FINALI

Il lavoro è accettabile soltanto se:

1. la soluzione compila in Debug e Release;
2. l’app parte senza eccezioni;
3. esiste una sola uscita audio master;
4. Deck A e Deck B sono miscelati nello stesso clock;
5. il crossfader lavora in equal-power;
6. il BPM sintetico rispetta la tolleranza;
7. la beat-grid è visibile e modificabile;
8. il follower può adattare il tempo al master;
9. l’avvio sincronizzato avviene su beat/bar;
10. il drift viene misurato e corretto;
11. AutoMix usa battute/frasi e non solo secondi;
12. il bass swap è implementato e disattivabile;
13. il master output non clippa nel test;
14. i cue sono salvati;
15. i loop restano quantizzati;
16. la UI è utilizzabile a 1366x768;
17. Spotify è chiaramente indicato come metadati;
18. un riferimento Spotify non viene caricato senza file locale;
19. il mapping al file locale è persistente;
20. i log permettono di diagnosticare gli errori;
21. tutti i test obbligatori passano;
22. README e documentazione rispecchiano il comportamento reale.

---

# FORMATO DEL WORKLOG

Dopo ogni blocco aggiorna `docs/WORKLOG.md`:

```markdown
## Fase X.Y — Titolo

### Obiettivo
...

### File modificati
- path/file.cs

### Implementazione
...

### Comandi eseguiti
- dotnet build ...

### Risultati
- Build: PASS/FAIL
- Test: PASS/FAIL
- Errori: ...

### Prossimo blocco
...
```

---

# FORMATO DELLA RISPOSTA FINALE

Alla fine rispondi con:

## Risultato

- stato reale;
- funzionalità completate;
- funzionalità non completate;
- eventuali limiti tecnici.

## File principali modificati

Elenco con percorso e motivazione.

## Build e test

Riporta i comandi realmente eseguiti e il risultato reale.

## Metriche audio

Riporta almeno:

```text
BPM test
errore fase medio
errore fase massimo
drift
peak dBFS
clipping count
```

## Avvio

Comandi esatti per aprire e avviare il progetto.

## Problemi residui

Non nascondere problemi. Se qualcosa non è verificato, scrivi esplicitamente:

```text
NON VERIFICATO
```

---

# COMANDO DI AVVIO DEL LAVORO

Inizia ora.

Non chiedere conferma generale.

Prima esegui FASE 0 e FASE 1, crea agenti, documenti e baseline. Poi prosegui automaticamente fase per fase finché la soluzione compila, i test passano e i criteri di accettazione risultano verificati.

Quando incontri un errore:

1. fermati nella fase corrente;
2. individua la causa reale;
3. applica la correzione minima;
4. ricompila;
5. aggiorna il worklog;
6. continua soltanto dopo esito positivo.

Non limitarti a descrivere cosa bisognerebbe fare: modifica realmente il progetto.
