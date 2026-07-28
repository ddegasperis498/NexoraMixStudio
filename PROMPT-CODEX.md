# PROMPT MASTER — NEXORA MIX STUDIO

Agisci come lead software architect, senior C#/.NET audio engineer, DSP engineer, UX designer e QA automation engineer. Devi lavorare sul repository **Nexora Mix Studio** senza riscrivere alla cieca ciò che è già funzionante.

## OBIETTIVO

Porta Nexora Mix Studio da MVP avanzato a workstation DJ professionale, moderna e più versatile di un normale software DJ, mantenendo un'architettura modulare, testabile e sicura. Il software deve restare apribile e compilabile con Visual Studio 2022/2026, .NET LTS e Windows 10/11.

## VINCOLO SPOTIFY NON NEGOZIABILE

Non implementare download, stream ripping, decrittazione, cattura del flusso audio, remix, sovrapposizione o alterazione dell'audio Spotify. Spotify può essere usato esclusivamente per autenticazione, metadati, riferimenti, playlist consentite e apertura del contenuto nell'app ufficiale. Il motore DJ deve lavorare soltanto su file locali, audio royalty-free, provider con licenza DJ/commerciale o sorgenti per cui l'utente possiede i diritti.

## REGOLE DI LAVORO

1. Analizza prima la soluzione esistente e riutilizza i componenti funzionanti.
2. Non eliminare funzionalità esistenti.
3. Non inserire segreti nel repository.
4. Non committare automaticamente.
5. Ogni modifica deve compilare con zero errori e zero warning rilevanti.
6. Aggiungi test unitari e di integrazione per ogni servizio non UI.
7. Mantieni UI fluida: niente analisi DSP sul thread grafico.
8. Usa cancellazione, progress reporting, logging strutturato e gestione errori.
9. Introduci dipendenze NuGet solo se attive, affidabili, compatibili e realmente necessarie.
10. Documenta migrazioni, configurazioni e limiti.

## ARCHITETTURA TARGET

Evolvi la soluzione verso:

- `NexoraMix.Domain`: entità e regole pure.
- `NexoraMix.Application`: use case, code/queue, sessioni e orchestrazione.
- `NexoraMix.AudioEngine`: graph audio real-time, mixer, routing e DSP.
- `NexoraMix.Analysis`: BPM, onset, downbeat, key, loudness, waveform e cache.
- `NexoraMix.Integrations`: Spotify metadata, provider musicali autorizzati, filesystem.
- `NexoraMix.Infrastructure`: SQLite, cache, logging, impostazioni protette.
- `NexoraMix.Desktop`: UI WPF o WinUI 3 con MVVM.
- `NexoraMix.Plugins.Abstractions`: contratti per effetti, controller e provider.
- `NexoraMix.Tests`: unit, integration, golden audio e performance test.

## FUNZIONI AUDIO PRIORITARIE

### Engine real-time

- un solo motore audio a bassa latenza con mixing float 32-bit;
- output WASAPI shared ed exclusive con selezione device;
- routing master, cue cuffie, booth e recording;
- gestione hot-plug dei dispositivi;
- protezione da clipping con limiter sul master;
- meter peak e RMS per deck/master;
- latenza misurabile e configurabile;
- nessuna allocazione evitabile nel callback audio.

### Deck

- 2 o 4 deck configurabili;
- play, pause, stop, slip mode, reverse e censor;
- pitch fader configurabile ±6/8/10/16/50/100%;
- key lock;
- time-stretch di alta qualità indipendente dal pitch;
- sync BPM, phase sync e beat sync;
- jog wheel virtuale con scratch;
- quantize configurabile;
- beat jump, loop roll, saved loop e auto-loop;
- almeno 16 hot cue colorati per traccia;
- cue point nominabili e commentabili;
- start cue, mix-in, mix-out, intro/outro e skip zone;
- waveform overview e waveform dettagliata zoomabile;
- beat-grid modificabile manualmente, con tap BPM e anchor beat.

### DSP

- trim/gain;
- EQ isolator a 3 o 4 bande;
- filtri HP/LP risonanti;
- crossfader curve configurabile;
- effetti per deck e master: echo, reverb, delay, flanger, phaser, roll, gate, beat repeat, filter, compressor e limiter;
- catene effetti salvabili;
- supporto plugin futuro tramite interfacce isolate, senza caricare plugin non attendibili nel processo UI.

## ANALISI MUSICALE

Realizza una pipeline offline incrementale con cache SQLite:

- fingerprint del file per evitare rianalisi inutili;
- waveform multi-risoluzione;
- BPM con confidenza;
- tempo variabile e beat tracking;
- downbeat/bar detection;
- tonalità musicale e notazione Camelot/Open Key;
- loudness LUFS-I, true peak, dynamic range;
- rilevamento silenzio iniziale/finale;
- riconoscimento intro, outro, breakdown, build-up e drop tramite euristiche verificabili;
- suggerimento automatico di start cue, mix-in e mix-out;
- correzione manuale sempre disponibile;
- elaborazione in background con coda, cancellazione e limite CPU.

Non usare contenuti Spotify per addestrare modelli o pipeline AI.

## SMART AUTOMIX

Implementa un planner trasparente e regolabile che assegni un punteggio a ogni possibile transizione considerando:

- differenza BPM e rapporti half/double-time;
- compatibilità armonica Camelot;
- energia e loudness;
- durata disponibile tra mix-in e mix-out;
- struttura musicale e allineamento sulle frasi da 4/8/16/32 battute;
- cronologia per evitare ripetizioni;
- preferenze dell'utente;
- tag, genere, rating e contesto della sessione.

L'utente deve poter vedere perché una transizione è stata proposta. Non creare una black box. Prevedi modalità Relax, Aperitivo, Dinner, Club, Workout e Custom.

Durante la transizione:

- pre-carica la traccia;
- allinea beat e frase;
- applica time-stretch entro limiti configurabili;
- esegui gain matching;
- usa EQ/filter transition preset;
- mostra anteprima grafica;
- consenti takeover manuale immediato.

## LIBRERIA

- SQLite locale con migrazioni;
- import cartelle e watch folder;
- drag & drop;
- lettura/scrittura tag quando sicuro;
- playlist, smart crate e filtri;
- ricerca istantanea;
- duplicati tramite hash e fingerprint;
- missing file relink;
- rating, colore, commenti e cronologia play;
- backup e restore;
- sessioni portabili con percorsi relativi opzionali.

## SPOTIFY E PROVIDER

Mantieni Spotify soltanto come catalogo/riferimento:

- OAuth Authorization Code con PKCE;
- redirect loopback `127.0.0.1`, mai `localhost`;
- token conservati con Windows Credential Manager o DPAPI;
- minimo numero di scope;
- gestione refresh token;
- import metadati di tracce/album/playlist solo quando l'API lo permette;
- messaggi chiari per 401, 403, 429 e restrizioni Developer Mode;
- comando “Collega file locale” con matching titolo/artista/durata;
- nessun tentativo di ottenere URL audio Spotify.

Crea un'interfaccia provider per aggiungere in futuro servizi che autorizzano esplicitamente l'uso DJ o commerciale.

## UI/UX

Crea una UI originale, non una copia di VirtualDJ:

- dark mode professionale con accent dinamico;
- layout responsive da 1366×768 a ultrawide/4K;
- DPI awareness e touch;
- pannelli ridimensionabili e workspace salvabili;
- modalità Performance, Library, Automix e Preparation;
- waveform GPU-accelerated se necessario;
- animazioni leggere disattivabili;
- accessibilità tastiera e screen reader;
- comandi rapidi modificabili;
- onboarding iniziale;
- indicatori chiari per clip, sync, key lock, quantize e device audio;
- nessun blocco UI durante import/analisi.

## CONTROLLER E AUTOMAZIONE

- MIDI learn;
- mapping JSON versionato;
- supporto HID futuro;
- OSC o WebSocket locale autenticato per remote control;
- API locale disattivata per default;
- macro e azioni concatenate;
- modalità kiosk/party protetta.

## RECORDING E BROADCAST

- registrazione master WAV/FLAC/MP3 dove legalmente e tecnicamente supportato;
- metadata sessione e cue sheet;
- split automatico tracce;
- normalizzazione non distruttiva;
- integrazione broadcast futura solo con sorgenti autorizzate;
- consenso e avvisi chiari.

## SICUREZZA

- secrets mai in chiaro nel repository;
- token protetti a riposo;
- validazione rigorosa di file e URL;
- niente esecuzione di comandi arbitrari;
- plugin isolati;
- dipendenze con lock file e audit;
- log senza token o dati sensibili;
- crash recovery e autosave atomico.

## TEST OBBLIGATORI

- parser Spotify;
- session serialization/migration;
- BPM su segnali sintetici 90/120/128/150;
- beat phase;
- automix scoring;
- crossfader curve;
- loop quantization;
- seek/cue boundary;
- file mancanti/corrotti;
- stress test analisi batch;
- test memoria su sessioni lunghe;
- test UI essenziali;
- benchmark callback audio e underrun.

Genera WAV sintetici nel test project; non includere musica protetta.

## DEFINITION OF DONE

Una fase è conclusa soltanto quando:

1. la soluzione compila in Debug e Release;
2. i test passano;
3. non ci sono deadlock o accessi UI cross-thread;
4. il progetto si avvia anche senza Spotify configurato;
5. l'assenza di device audio o codec produce un errore comprensibile;
6. README, changelog e guida migrazione sono aggiornati;
7. le funzionalità Spotify rispettano i limiti dichiarati;
8. è fornito un riepilogo dei file modificati, dei test eseguiti e dei rischi residui.

## ORDINE DI IMPLEMENTAZIONE

Procedi in piccoli incrementi compilabili:

1. correggi eventuali errori dell'MVP e aggiungi test;
2. persistenza SQLite e cache analisi;
3. audio graph unico e meter;
4. beat-grid editor e hot cue;
5. time-stretch/key lock;
6. key/loudness/structure analysis;
7. smart AutoMix con spiegazione punteggi;
8. MIDI learn;
9. recording;
10. plugin/provider architecture.

Al termine di ogni incremento mostra soltanto: modifiche effettuate, file coinvolti, comandi di build/test, risultati reali e problemi ancora aperti. Non dichiarare riuscita una build che non hai eseguito.
