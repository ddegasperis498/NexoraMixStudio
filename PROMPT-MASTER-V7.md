# PROMPT MASTER DEFINITIVO — NEXORA MIX STUDIO V7 PRO DJ
# 4-DECK AUTO MASHUP + PROFESSIONAL CONTROLLER EDITION

## 0. MISSIONE

Trasforma il progetto esistente **Nexora Mix Studio** in un software DJ professionale per Windows, moderno, stabile, modulare e realmente utilizzabile dal vivo.

Il prodotto deve supportare:

- mixaggio fino a 4 deck;
- Auto Mix;
- Auto Mashup da 2, 3 o 4 tracce;
- rilevamento BPM;
- beat-grid;
- phrase-grid;
- sincronizzazione BPM/fase/frase;
- waveform avanzate;
- hot cue;
- loop;
- sampler;
- effetti;
- remix;
- stems;
- registrazione;
- libreria;
- MIDI/HID;
- controller DJ professionali;
- console e lettori CDJ;
- mapping personalizzato;
- profili hardware;
- feedback LED/display quando tecnicamente disponibile.

Repository previsto:

```text
C:\Users\domen\Qsync\Progetti\AreaSviluppo\NexoraMixStudio
```

Soluzione:

```text
NexoraMixStudio.sln
```

Progetti principali:

```text
NexoraMix.App
NexoraMix.Core
NexoraMix.Audio
```

Progetti di test da mantenere o creare:

```text
NexoraMix.Core.Tests
NexoraMix.Audio.Tests
NexoraMix.IntegrationTests
NexoraMix.PerformanceTests
NexoraMix.ControllerTests
NexoraMix.SelfTest
```

Non creare una demo separata. Lavora sulla soluzione reale.

---

# 1. MODELLO DISPONIBILE E METODO DI LAVORO

Il motore Codex disponibile usa `gpt-oss:20b`.

Regole obbligatorie:

1. lavorare in blocchi piccoli;
2. modificare massimo 4–6 file per blocco;
3. compilare dopo ogni blocco;
4. eseguire test della fase;
5. correggere gli errori prima di continuare;
6. aggiornare `docs/WORKLOG.md`;
7. non rileggere inutilmente tutto il repository;
8. non eseguire agenti concorrenti sugli stessi file;
9. non creare funzioni finte;
10. non lasciare pulsanti senza comportamento;
11. non inventare pacchetti NuGet;
12. non inventare protocolli hardware;
13. non dichiarare supportato un controller senza test o mapping verificabile;
14. non dichiarare HID completo se è disponibile soltanto MIDI;
15. non dichiarare display/LED feedback se il dispositivo o il protocollo non lo consente;
16. non usare `catch { }` vuoti;
17. non nascondere eccezioni;
18. non eseguire commit, push o pull request;
19. creare backup prima delle modifiche strutturali;
20. mantenere compatibilità Windows 10/11 x64;
21. privilegiare stabilità, latenza e prevedibilità;
22. se Codex non supporta veri sub-agent, simulare i ruoli in sequenza;
23. ogni agente deve scrivere un rapporto;
24. ogni funzione audio/hardware deve avere test o procedura riproducibile;
25. non dichiarare “Auto Mashup” se viene eseguito soltanto un crossfade.

---

# 2. OBIETTIVO FINALE

Creare **Nexora Mix Studio V7 Pro DJ — 4-Deck Auto Mashup & Controller Edition**.

Il prodotto deve:

- compilare in Debug e Release;
- avviarsi senza eccezioni;
- avere una sola pipeline audio master;
- gestire 4 deck nello stesso clock;
- riprodurre fino a 4 file locali simultanei;
- sincronizzare BPM/fase/frase;
- creare mashup automatici;
- gestire ruoli musicali;
- permettere controllo manuale completo;
- supportare controller DJ popolari tramite MIDI/HID;
- permettere MIDI Learn;
- permettere mapping import/export;
- mostrare dispositivo collegato e stato mapping;
- gestire hot plug;
- gestire perdita dispositivo;
- supportare LED feedback dove disponibile;
- supportare jog wheel, pitch, fader, knob, pad, encoder e pulsanti;
- supportare console all-in-one e lettori CDJ come controller esterni;
- supportare più controller contemporaneamente;
- salvare profili hardware;
- registrare master e mashup;
- mantenere clipping a zero nei test;
- avere UI leggibile a 100/125/150/175%;
- non presentare Spotify come sorgente audio mixabile.

---

# 3. MODALITÀ OPERATIVE

## 3.1 Classic DJ

- Deck A/B;
- mixer tradizionale;
- crossfader;
- cue/PFL;
- hot cue;
- loop;
- sync;
- EQ;
- filtro;
- effetti.

## 3.2 Four Deck

- Deck A/B/C/D;
- clock condiviso;
- assegnazione crossfader;
- fader indipendenti;
- cue indipendenti;
- master selezionabile;
- sync indipendente;
- waveform simultanee.

## 3.3 AutoMix

- coda;
- analisi BPM/key/energia;
- ingressi su frase;
- EQ swap;
- transizioni automatiche;
- cronologia.

## 3.4 Auto Mashup

Gestire 2–4 tracce.

Il software deve:

1. analizzare ogni traccia;
2. scegliere il master;
3. sincronizzare BPM;
4. allineare fase;
5. allineare frase;
6. assegnare ruoli;
7. evitare collisioni vocali;
8. evitare collisioni di basso;
9. applicare EQ/filtri;
10. gestire gain e headroom;
11. creare ingressi progressivi;
12. permettere override manuale;
13. registrare il risultato;
14. mostrare metriche reali.

Ruoli:

```text
Foundation
Drums
Bass
Vocals
Instrumental
Texture
Percussion
FX Layer
Acapella
Loop
Full Mix
```

---

# 4. CONTROLLER E CONSOLE SUPPORTATE

## 4.1 Obiettivo generale

Il software deve essere progettato per collegarsi ai controller e alle console DJ più diffuse tramite:

- MIDI standard;
- MIDI over USB;
- HID quando documentato e tecnicamente accessibile;
- protocollo vendor solo se documentato o ottenuto tramite SDK ufficiale;
- output LED/display quando supportato;
- audio interface separata dal controllo;
- più dispositivi contemporaneamente.

## 4.2 Famiglie di dispositivi da prevedere

Creare profili e infrastruttura per:

```text
Pioneer DJ / AlphaTheta
CDJ e XDJ
DDJ
DJM con MIDI
Gemini
Denon DJ
Numark
Hercules
Reloop
Native Instruments Traktor Kontrol
Roland DJ
Akai
Novation
Behringer
Allen & Heath con MIDI
controller MIDI generici
tastiere MIDI
pad controller
footswitch MIDI
```

Non dichiarare compatibilità totale per ogni modello senza test.

Ogni profilo deve indicare:

```text
Verified
Community Tested
Experimental
MIDI Generic
HID Partial
Unsupported Feature
```

## 4.3 CDJ / XDJ / console esterne

Prevedere tre modalità:

### A. MIDI Controller Mode

Il lettore o la console invia:

- play/pause;
- cue;
- jog;
- pitch;
- loop;
- hot cue;
- browse;
- load;
- sync;
- beat jump;
- tempo reset.

### B. HID Advanced Mode

Quando il protocollo è disponibile:

- jog ad alta risoluzione;
- posizione traccia;
- waveform/display;
- tempo;
- time remaining;
- track title;
- artwork se supportato;
- LED;
- ring jog;
- pad colors;
- encoder feedback.

### C. External Deck / Timecode Ready Architecture

Preparare astrazione per:

- deck esterno;
- audio input;
- timecode control;
- DVS futuro;
- beat detection da input;
- sync assistita;
- no implementazione DVS finta.

## 4.4 Controller multipli

Supportare scenari:

```text
1 controller all-in-one
2 CDJ + mixer
4 CDJ
1 controller + pad controller
1 controller + MIDI keyboard
1 controller + footswitch
controller principale + controller FX
```

Ogni dispositivo deve poter essere assegnato a:

```text
Deck A
Deck B
Deck C
Deck D
Mixer
Sampler
FX
Library
Master
Global
```

## 4.5 Profili hardware

Ogni profilo deve essere serializzato:

```csharp
public sealed record ControllerProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Manufacturer { get; init; }
    public required string ProductMatch { get; init; }
    public ControllerProtocol Protocol { get; init; }
    public ControllerVerificationStatus VerificationStatus { get; init; }
    public required IReadOnlyList<ControlMapping> Mappings { get; init; }
    public required IReadOnlyList<FeedbackMapping> FeedbackMappings { get; init; }
}
```

Stati verifica:

```text
BuiltInVerified
BuiltInExperimental
UserCreated
Community
GenericMidi
Disabled
```

## 4.6 MIDI Learn

Implementare:

- seleziona comando software;
- muovi controllo hardware;
- rileva messaggio;
- assegna;
- salva;
- mostra conflitti;
- consente curva;
- consente inversione;
- consente dead zone;
- consente sensibilità;
- consente soft takeover;
- consente range;
- consente modifier/shift;
- consente layer;
- consente feedback LED.

Tipi mapping:

```text
Button
Toggle
Momentary
AbsoluteKnob
RelativeEncoder
Fader
PitchFader
JogWheel
VelocityPad
Aftertouch
PitchBend
ProgramChange
Modifier
ShiftLayer
```

## 4.7 Jog wheel

Supportare:

- scratch;
- nudge;
- seek;
- vinyl mode;
- CDJ mode;
- touch sensor;
- platter speed;
- brake;
- start time;
- sensitivity;
- high-resolution input;
- fallback MIDI low-resolution;
- smoothing;
- no jump improvvisi;
- no drift cumulativo.

## 4.8 Pitch fader

Supportare:

- range ±4%;
- ±6%;
- ±8%;
- ±10%;
- ±16%;
- ±25%;
- ±50%;
- wide;
- tempo reset;
- center detent;
- soft takeover;
- pickup;
- fine mode;
- 0.01% internal precision quando possibile.

## 4.9 Performance pad

Pad modes:

```text
Hot Cue
Beat Loop
Saved Loop
Beat Jump
Sampler
Slicer
Slicer Loop
Key Shift
Keyboard
Stems
FX Trigger
Roll
User Mode
```

Supportare:

- velocity;
- color feedback;
- shift layer;
- page/bank;
- quantize;
- latch/hold;
- choke;
- aftertouch se disponibile.

## 4.10 LED e display

Implementare feedback solo quando disponibile:

- play state;
- cue state;
- sync;
- master;
- loop;
- hot cue colors;
- pad colors;
- meter;
- deck assignment;
- FX state;
- recording;
- warning;
- track loaded;
- time remaining;
- BPM;
- key.

Nessun polling aggressivo.

Throttling feedback:

```text
meter: 20–30 Hz
time display: 5–10 Hz
static state: on change
waveform/display: solo se protocollo e performance lo consentono
```

## 4.11 Audio interface controller

Separare:

```text
Control device
Audio output device
Headphone output
Booth output
Microphone input
External input
```

Permettere:

- WASAPI shared;
- WASAPI exclusive;
- ASIO opzionale;
- device selection;
- channel routing;
- master L/R;
- headphones L/R;
- booth;
- external input;
- test signal;
- latency setup;
- buffer setup;
- safe fallback.

---

# 5. LIMITI SUI CONTENUTI

## File locali

Il vero mix usa file locali autorizzati:

```text
WAV
MP3
FLAC
AIFF
AAC
M4A
WMA
OGG se supportato
```

## Spotify

Spotify è soltanto:

- metadati;
- playlist;
- apertura player esterno;
- associazione file locale.

Vietato:

- download;
- stream ripping;
- estrazione PCM;
- mix diretto;
- waveform finta;
- sync finto;
- EQ finto;
- mashup finto.

---

# 6. ORGANIZZAZIONE DEGLI AGENTI

Crea:

```text
AGENTS.md
.codex\agents\00-leader.md
.codex\agents\01-repository-auditor.md
.codex\agents\02-chief-architect.md
.codex\agents\03-audio-engine.md
.codex\agents\04-dsp-effects.md
.codex\agents\05-beat-sync-analysis.md
.codex\agents\06-auto-mashup.md
.codex\agents\07-remix-sampler.md
.codex\agents\08-stems-engine.md
.codex\agents\09-ui-ux.md
.codex\agents\10-library-spotify.md
.codex\agents\11-controller-integration.md
.codex\agents\12-recording-export.md
.codex\agents\13-qa-performance.md
.codex\agents\14-security-reliability.md
.codex\agents\15-documentation-release.md
```

Crea:

```text
docs\IMPLEMENTATION_PLAN.md
docs\WORKLOG.md
docs\ARCHITECTURE.md
docs\AUDIO_ENGINE.md
docs\DSP_EFFECTS.md
docs\SYNC_ENGINE.md
docs\AUTO_MASHUP.md
docs\REMIX_ENGINE.md
docs\STEMS_ENGINE.md
docs\UI_GUIDELINES.md
docs\LIBRARY.md
docs\CONTROLLERS.md
docs\MIDI_MAPPING.md
docs\RECORDING.md
docs\TEST_PLAN.md
docs\PERFORMANCE_BUDGET.md
docs\RELEASE_CHECKLIST.md
docs\agent-reports
```

---

# 7. AGENTE 00 — LEADER / ORCHESTRATORE

Responsabilità:

- creare piano;
- assegnare lavoro;
- controllare ownership;
- impedire conflitti;
- controllare build/test;
- rifiutare funzioni simulate;
- rifiutare compatibilità hardware non testata;
- rifiutare HID dichiarato senza prova;
- aggiornare worklog;
- produrre report finale.

Output:

```text
docs\agent-reports\00-leader.md
```

---

# 8. AGENTE 01 — REPOSITORY AUDITOR

Responsabilità:

- SDK;
- progetti;
- dipendenze;
- build baseline;
- warning;
- binding;
- doppi clock;
- dead code;
- duplicati;
- path assoluti;
- problemi DPI;
- problemi thread;
- vecchi hotfix;
- codice controller esistente;
- dipendenze MIDI esistenti;
- codice HID esistente;
- device enumeration.

Comandi:

```powershell
dotnet --info
dotnet restore .\NexoraMixStudio.sln
dotnet build .\NexoraMixStudio.sln -c Debug
dotnet build .\NexoraMixStudio.sln -c Release
dotnet test .\NexoraMixStudio.sln -c Debug
```

Output:

```text
docs\agent-reports\01-repository-auditor.md
```

---

# 9. AGENTE 02 — CHIEF ARCHITECT

Responsabilità:

- confini App/Core/Audio;
- contratti;
- stati;
- eventi;
- plugin model;
- controller abstraction;
- audio device abstraction;
- persistence;
- error model;
- capabilities;
- versioning.

Interfacce minime:

```csharp
public interface IControllerManager
{
    IReadOnlyList<ControllerDeviceInfo> Devices { get; }
    IReadOnlyList<ControllerConnection> Connections { get; }

    Task RefreshAsync(CancellationToken cancellationToken);
    Task<ControllerConnection> ConnectAsync(
        ControllerDeviceInfo device,
        ControllerProfile profile,
        CancellationToken cancellationToken);

    Task DisconnectAsync(
        string connectionId,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IControllerInputRouter
{
    void Route(ControllerInputEvent inputEvent);
}
```

```csharp
public interface IControllerFeedbackRouter
{
    ValueTask SendAsync(
        ControllerFeedbackEvent feedback,
        CancellationToken cancellationToken);
}
```

Output:

```text
docs\ARCHITECTURE.md
docs\agent-reports\02-chief-architect.md
```

---

# 10. AGENTE 03 — AUDIO ENGINE

Responsabilità:

- una pipeline;
- quattro deck;
- master clock;
- WASAPI;
- ASIO adapter opzionale;
- cue/PFL;
- booth;
- routing;
- limiter;
- meter;
- underrun;
- device hot swap;
- safe dispose.

Output:

```text
docs\AUDIO_ENGINE.md
docs\agent-reports\03-audio-engine.md
```

---

# 11. AGENTE 04 — DSP ED EFFETTI

Effetti minimi:

```text
Filter
Echo
Delay
Reverb
Flanger
Phaser
Chorus
Bit Crusher
Distortion
Saturation
Compressor
Gate
Roll
Beat Repeat
Vinyl Brake
Vinyl Start
Backspin
Tape Stop
Pitch Echo
Trans Gate
Noise Sweep
Low Cut Echo
High Cut Echo
Loop Roll
Stutter
Pan
Auto Pan
LFO Filter
```

Output:

```text
docs\DSP_EFFECTS.md
docs\agent-reports\04-dsp-effects.md
```

---

# 12. AGENTE 05 — BPM / SYNC / PHRASE

Responsabilità:

- BPM;
- beat-grid;
- phrase-grid;
- key;
- energy;
- sync;
- drift;
- phase;
- phrase alignment;
- tempo ratio;
- pitch bend.

Output:

```text
docs\SYNC_ENGINE.md
docs\agent-reports\05-beat-sync-analysis.md
```

---

# 13. AGENTE 06 — AUTO MASHUP

Responsabilità:

- 2–4 deck;
- compatibility score;
- role assignment;
- master selection;
- timeline;
- phrase alignment;
- EQ automation;
- bass collision control;
- vocal collision control;
- headroom;
- preview;
- recording.

Output:

```text
docs\AUTO_MASHUP.md
docs\agent-reports\06-auto-mashup.md
```

---

# 14. AGENTE 07 — REMIX / SAMPLER

Responsabilità:

- sampler;
- pad;
- slicer;
- loop capture;
- sequencer;
- scenes;
- snapshots;
- undo/redo.

Output:

```text
docs\REMIX_ENGINE.md
docs\agent-reports\07-remix-sampler.md
```

---

# 15. AGENTE 08 — STEMS

Responsabilità:

- provider;
- separazione offline;
- cache;
- vocals/drums/bass/other;
- mute/solo;
- FX per stem;
- no fake stems.

Output:

```text
docs\STEMS_ENGINE.md
docs\agent-reports\08-stems-engine.md
```

---

# 16. AGENTE 09 — UI/UX

Responsabilità:

- Classic;
- 4 Deck;
- Performance;
- Mashup;
- Remix;
- Library;
- Preparation;
- Recording;
- Controller Setup;
- MIDI Learn;
- Device Routing;
- responsive;
- DPI;
- nessun testo tagliato;
- binding readonly OneWay;
- input TwoWay;
- no audio logic nel code-behind.

Output:

```text
docs\UI_GUIDELINES.md
docs\agent-reports\09-ui-ux.md
```

---

# 17. AGENTE 10 — LIBRERIA E SPOTIFY

Responsabilità:

- libreria;
- crates;
- smart playlist;
- mapping locale;
- Spotify metadata;
- no audio extraction.

Output:

```text
docs\LIBRARY.md
docs\agent-reports\10-library-spotify.md
```

---

# 18. AGENTE 11 — CONTROLLER INTEGRATION SPECIALIST

## Missione

Implementare l’intero sottosistema MIDI/HID e i profili hardware.

## Ownership

```text
NexoraMix.Core\Controllers
NexoraMix.App\Controllers
NexoraMix.ControllerTests
docs\CONTROLLERS.md
docs\MIDI_MAPPING.md
```

## Compiti

### Device discovery

- enumerare MIDI input;
- enumerare MIDI output;
- rilevare hot plug;
- riconoscere manufacturer/product;
- associare profilo;
- gestire più dispositivi;
- mostrare stato;
- gestire reconnect.

### MIDI input

- Note On/Off;
- Control Change;
- Pitch Bend;
- Program Change;
- Aftertouch;
- SysEx solo se necessario e documentato;
- NRPN/RPN;
- 14-bit CC;
- relative encoder modes;
- timestamp;
- debounce;
- smoothing.

### MIDI output

- LED;
- pad color;
- ring;
- meter;
- display se protocollo supportato;
- feedback state;
- throttling.

### HID

- creare astrazione;
- enumerare device;
- report descriptor;
- input report;
- output report;
- feature report;
- no reverse engineering distruttivo;
- no protocollo inventato;
- profilo sperimentale separato;
- log raw opzionale per sviluppo;
- privacy e sicurezza.

### Profili iniziali

Creare profili generici e struttura per:

```text
Pioneer DJ / AlphaTheta DDJ family
Pioneer DJ / AlphaTheta CDJ/XDJ controller modes
Gemini controller family
Denon DJ controller/player family
Numark controller family
Hercules DJControl family
Reloop controller family
Native Instruments Traktor Kontrol family
Roland DJ family
Akai/Novation pad controllers
Generic MIDI 2-deck
Generic MIDI 4-deck
Generic MIDI mixer
Generic MIDI pads
```

Ogni profilo deve riportare capacità e stato reale.

### Controller wizard

UI:

1. seleziona dispositivo;
2. scegli profilo;
3. testa input;
4. assegna deck;
5. testa output;
6. calibra jog;
7. calibra pitch;
8. calibra fader;
9. configura audio;
10. salva.

### MIDI Learn

- comando software;
- movimento hardware;
- rilevamento;
- conferma;
- curva;
- range;
- invert;
- dead zone;
- soft takeover;
- modifier;
- layer;
- feedback.

### Jog calibration

Misurare:

- ticks per giro;
- touch;
- velocità;
- direzione;
- risoluzione;
- jitter;
- smoothing;
- scratch sensitivity;
- nudge sensitivity.

### Pitch calibration

- min;
- center;
- max;
- detent;
- direction;
- resolution;
- pickup threshold.

### Controller test console

Creare una finestra diagnostica:

- raw MIDI;
- parsed command;
- deck assignment;
- last input;
- rate;
- jitter;
- feedback;
- connection errors;
- export log.

Output:

```text
docs\CONTROLLERS.md
docs\MIDI_MAPPING.md
docs\agent-reports\11-controller-integration.md
```

---

# 19. AGENTE 12 — RECORDING / EXPORT

Responsabilità:

- master recording;
- mashup recording;
- deck recording;
- WAV 16/24/32 float;
- recovery;
- markers;
- offline export.

Output:

```text
docs\RECORDING.md
docs\agent-reports\12-recording-export.md
```

---

# 20. AGENTE 13 — QA / PERFORMANCE

Test obbligatori:

## Controller tests

- device discovery mock;
- connect/disconnect;
- hot plug;
- duplicate device;
- MIDI note;
- MIDI CC;
- pitch bend;
- 14-bit;
- relative encoder;
- jog smoothing;
- pitch calibration;
- soft takeover;
- mapping conflict;
- LED feedback;
- multiple controllers;
- reconnect;
- malformed packet;
- high-rate input;
- no memory leak.

## 4-deck controller scenario

Simulare:

```text
2 CDJ/controller deck units
1 external mixer
1 pad controller
```

Verificare:

- assegnazione;
- input routing;
- feedback;
- deck switching;
- master;
- sync;
- hot cue;
- loop;
- sampler;
- recording.

## Performance

- input controller >= 1000 eventi/s senza blocco UI;
- feedback throttled;
- no allocation pressure anomala;
- no UI freeze;
- audio callback non bloccato;
- controller disconnect non interrompe audio.

Output:

```text
docs\TEST_PLAN.md
docs\PERFORMANCE_BUDGET.md
docs\agent-reports\13-qa-performance.md
```

---

# 21. AGENTE 14 — SECURITY / RELIABILITY

Responsabilità:

- token storage;
- safe file access;
- controller profile validation;
- malformed MIDI/HID handling;
- log rotation;
- crash recovery;
- no telemetry nascosta;
- no arbitrary script execution;
- safe SysEx limits;
- safe device disconnect.

Output:

```text
docs\agent-reports\14-security-reliability.md
```

---

# 22. AGENTE 15 — DOCUMENTAZIONE / RELEASE

Aggiornare:

```text
README.md
CHANGELOG.md
docs\USER_GUIDE.md
docs\CONTROLLERS.md
docs\MIDI_MAPPING.md
docs\RELEASE_CHECKLIST.md
```

Documentare:

- controller verificati;
- controller sperimentali;
- generic MIDI;
- MIDI Learn;
- CDJ/XDJ mode;
- jog calibration;
- pitch calibration;
- audio routing;
- mapping import/export;
- limiti reali.

Output:

```text
docs\agent-reports\15-documentation-release.md
```

---

# 23. FASI

## Fase 0

- backup;
- agenti;
- baseline;
- build;
- test.

## Fase 1

- correggere errori;
- startup stabile;
- UI leggibile.

## Fase 2

- architettura 4 deck;
- pipeline unica.

## Fase 3

- audio engine;
- routing;
- cue/PFL.

## Fase 4

- BPM/sync/phrase.

## Fase 5

- Auto Mashup.

## Fase 6

- DSP.

## Fase 7

- Remix/Sampler.

## Fase 8

- Stems.

## Fase 9

- UI completa.

## Fase 10

- libreria/Spotify.

## Fase 11

- controller/MIDI/HID.

## Fase 12

- registrazione.

## Fase 13

- hardening.

## Fase 14

- release.

---

# 24. CRITERI DI ACCETTAZIONE CONTROLLER

Il supporto hardware è accettabile soltanto se:

1. il device viene enumerato;
2. il device si connette;
3. il profilo viene riconosciuto;
4. i messaggi vengono decodificati;
5. play/cue funzionano;
6. jog funziona senza salti;
7. pitch funziona;
8. fader funziona;
9. knob funziona;
10. pad funzionano;
11. mapping viene salvato;
12. mapping viene ricaricato;
13. MIDI Learn funziona;
14. soft takeover funziona;
15. più controller funzionano insieme;
16. la disconnessione non interrompe l’audio;
17. il reconnect funziona;
18. LED feedback funziona dove supportato;
19. lo stato sperimentale è dichiarato;
20. nessun controller viene dichiarato “supportato” senza prova.

---

# 25. CRITERI GENERALI

Il lavoro è completato solo se:

- Debug compila;
- Release compila;
- test passano;
- app parte;
- 4 deck suonano insieme;
- Sync funziona;
- Auto Mashup funziona;
- effetti funzionano;
- controller subsystem funziona;
- UI non taglia contenuti;
- Spotify è trattato correttamente;
- registrazione funziona;
- metriche sono reali;
- documentazione coincide con il comportamento.

---

# 26. WORKLOG

Dopo ogni blocco:

```markdown
## Fase X.Y — Titolo

### Agente
...

### Obiettivo
...

### File modificati
- ...

### Build
PASS/FAIL

### Test
PASS/FAIL

### Metriche
...

### Problemi
...

### Prossimo blocco
...
```

---

# 27. RISPOSTA FINALE

Riportare:

- stato reale;
- funzionalità completate;
- funzionalità non completate;
- controller verificati;
- controller sperimentali;
- protocolli usati;
- build;
- test;
- metriche audio;
- metriche controller;
- limiti;
- istruzioni avvio.

Scrivere `NON VERIFICATO` dove necessario.

---

# 28. COMANDO DI AVVIO

Inizia ora.

Non chiedere conferma generale.

Esegui Fase 0 e Fase 1.

Crea agenti, documenti, backup e baseline.

Procedi fase per fase.

Quando una fase fallisce:

1. fermati;
2. trova la causa;
3. correggi;
4. ricompila;
5. testa;
6. aggiorna worklog;
7. continua soltanto dopo PASS.

Non limitarti a descrivere cosa fare.

Modifica realmente il progetto.

Non dichiarare Nexora Mix Studio V7 Pro DJ completato finché i criteri di accettazione non sono verificati.
