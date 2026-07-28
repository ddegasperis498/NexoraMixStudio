# Worklog V8

## Fase 8.1 — UI desktop

- Introdotti DeckPanel, MixerPanel e ChannelStrip.
- Aumentata leggibilità dei testi.
- Separate le funzioni in schede.
- Resi visibili Loop, Hot Cue, Effetti e Mixer.

## Fase 8.2 — Console tablet

- Aggiunto TabletRemoteHost con HTTP/WebSocket.
- Aggiunti snapshot per quattro deck.
- Aggiunti comandi remoti per trasporto, BPM, EQ, FX e crossfader.
- Creata UI responsive senza dipendenze JavaScript esterne.
- Aggiunta animazione piatti con requestAnimationFrame.

## Fase 8.3 — Android

- Creato progetto separato `NexoraMix.Tablet.Android`.
- Aggiunto WebView wrapper con indirizzo PC persistente.
- Abilitato traffico HTTP locale per rete privata.

## Fase 8.4 — Import musica

- Creata/gestita sottocartella `Inbox` dentro la cartella Musica dell'utente Windows.
- Aggiunto FileSystemWatcher.
- Nessun downloader YouTube o stream ripping integrato.

## Fase 8.5 — Stabilizzazione Sync audio

- Corretto `TempoSampleProvider`: la compattazione del buffer non avviene più a ogni campione, riducendo i picchi CPU che causavano scatti quando Sync variava il tempo.
- Aggiunto ramp morbido del playback rate per evitare salti bruschi di pitch/tempo.
- Reso il servo di fase Sync più conservativo con correzione limitata e slew limit sul rapporto tempo applicato.
- Aggiunto self-test offline di aggancio Sync master/follower con verifica di audio continuo e campioni validi.

## Fase 8.6 — Controlli performance e layout tablet

- Aggiunto CUT momentaneo su desktop e tablet: premuto riparte dal cue, rilasciato torna al cue; se durante il CUT premi PLAY, la traccia resta in riproduzione.
- Aggiunto zoom waveform con rotella mouse e pinch touch; il click/tap cerca nella finestra zoomata.
- La waveform desktop e tablet mostra la beat-grid nella porzione zoomata, con misure leggibili quando ingrandita.
- Aggiunti pulsanti sempre visibili per caricare la traccia selezionata nei Deck A/B/C/D.
- Se un master locale sta già suonando, il caricamento di una traccia in un altro deck arma il Sync sulla prossima misura e imposta il BPM del master.
- Sync manuale ora arma sulla prossima misura invece che sulla prossima battuta singola.
- Reso più elastico il layout Console desktop per stare nello schermo in fullscreen.
- Compattato il layout tablet landscape per mostrare almeno due deck e mixer nello spazio disponibile.

## Fase 8.7 — Uscite audio e preascolto

- Aggiunto bus CUE/cuffie separato dal master.
- Aggiunta scelta dispositivo audio Windows per OUT MASTER e OUT CUFFIE.
- Aggiunto volume preascolto.
- Aggiunto CUE per singolo deck nel mixer desktop e nella console tablet.
- Aggiunto CUE MASTER.
- Il preascolto deck usa il segnale processato pre-crossfader, quindi puoi ascoltare in cuffia anche un deck chiuso dal crossfader.
- Aggiunto tap del master per preascolto master senza consumare il flusso audio principale.
- Raccomandazione tecnica: usare una scheda audio/controller con due uscite fisiche sul PC; l'uscita audio del tablet via Wi-Fi non è adatta al preascolto DJ per latenza e jitter.

## Fase 8.8 — UI leggibile, caricamento diretto e Sync live

- Corretto il Sync su due tracce già in play: il deck follower non viene più riavviato e non torna all'inizio; viene agganciato live con correzione BPM/fase.
- Il Sync su deck fermo resta quantizzato alla prossima misura, così il caricamento automatico può ancora entrare a battuta.
- Aggiunto self-test dedicato: due deck in play, Sync live sul follower e verifica che la posizione non venga riportata a zero.
- Aggiunto pulsante grande `CARICA QUI` dentro ogni deck per caricare subito la traccia selezionata in quel deck.
- Aggiunto drag and drop diretto sul deck: trascinando un file audio sopra Deck A/B/C/D viene importato e caricato proprio su quel deck.
- Migliorata la leggibilità desktop: tab scure anche quando selezionate, pulsanti disabilitati ancora leggibili, combo audio scure ad alto contrasto, mixer centrale più largo e testi più marcati.
- Le tendine OUT MASTER e OUT CUFFIE nel mixer sono ora su righe separate, con template scuro e larghezza piena per evitare testo bianco su fondo chiaro e nomi dispositivo tagliati.
- La libreria è esplicitamente in sola lettura per evitare binding WPF indesiderati su proprietà calcolate.
- Aggiornato il validatore statico per ignorare `bin/obj` e per riconoscere le due uscite WaveOutEvent separate: master e cuffie.

## Fase 8.9 — Branding Nexora

- Aggiunta l'immagine Nexora fornita come asset ufficiale dell'app desktop.
- Generata icona Windows `.ico` e collegata all'eseguibile NexoraMix.
- Sostituito il vecchio riquadro con lettera `N` nella console desktop con il nuovo logo.
- Aggiunto il logo nella schermata di abbinamento e nella topbar della console tablet/web.
- Aggiunte icone PWA `192x192` e `512x512` nel manifest della console tablet.
- Generate le icone launcher Android per densità `mipmap-mdpi`, `hdpi`, `xhdpi`, `xxhdpi`, `xxxhdpi` e collegate al manifest Android.

## Fase 8.10 — Console tablet più maneggevole

- Separata la vista mixer dalla vista deck: in modalità `2 DECK` e `4 DECK` il mixer non resta più sotto ai piatti, riducendo confusione e scroll.
- La vista `MIXER` ora mostra il mixer come superficie dedicata, con deck nascosti.
- Su tablet/touch la barra superiore è più compatta e i pulsanti vista sono più grandi.
- La barra master tablet mostra solo le informazioni operative principali e `STOP TUTTO`, nascondendo comandi secondari che occupavano spazio.
- In landscape tablet i due deck principali stanno nello schermo con piatti, waveform, trasporto e pannello performance attivo.
- Aumentata la dimensione dei comandi touch principali: PLAY, CUT, SYNC, STOP, tab performance, pad loop/hot cue.
- In modalità 4 deck il layout resta a griglia scorrevole, mentre la modalità 2 deck resta focalizzata su A/B.

## Fase 8.11 — Mixer rapido tablet e touch DJ

- Aggiunta una barra mixer rapida nelle viste deck tablet con `CUE A`, `CUE B`, crossfader A/B e pulsante `CENTRA`.
- Il crossfader rapido resta sincronizzato con lo stato master e invia i comandi senza dover aprire la vista mixer completa.
- La vista mixer dedicata mantiene il mixer completo, mentre le viste deck restano più leggere e usabili.
- Disattivato il menu copia/selezione testo sui controlli della console tablet: il tocco lungo su CUE, CUT, PLAY, piatti e waveform non apre più la finestra Android per copiare.
- Aggiunto anche il blocco nativo nel wrapper Android WebView: la pressione lunga viene consumata dall'app e non apre il menu contestuale del sistema.

## Fase 8.12 — QR di collegamento tablet

- Aggiunto QR nella scheda `TABLET / ANDROID` del programma Windows.
- Il QR contiene indirizzo locale del PC e codice di abbinamento temporaneo.
- Scansionando il QR dal tablet, la console web apre la URL e inserisce automaticamente il codice senza doverlo digitare.
- Il QR viene generato solo in locale quando la console tablet è avviata e viene rimosso quando il server tablet viene arrestato.

## Fase 8.13 — Caricamento automatico cartella Musica

- All'avvio Nexora importa automaticamente tutti i file audio supportati presenti nella cartella Musica dell'utente Windows, incluse le sottocartelle.
- Il pulsante `APRI CARTELLA MUSICA` apre ora la cartella `Music` principale, non solo `Music\Inbox`.
- Il controllo automatico dei nuovi file osserva tutta la cartella `Music` con sottocartelle incluse.
- I duplicati gia' presenti in libreria vengono ignorati come prima.

## Fase 8.14 — Cartella Musica utente Windows

- La cartella predefinita non e' piu' `%LOCALAPPDATA%\NexoraMix\Music`.
- Nexora usa `Environment.SpecialFolder.MyMusic`, quindi la vera cartella Musica dell'utente Windows.
- Se Windows non restituisce il percorso Musica, viene usato il fallback `C:\Users\<utente>\Music`.

## Verifica

- XML/XAML: PASS statico.
- JavaScript: PASS `node --check` con runtime Node integrato Codex.
- Delimitatori C#: PASS statico.
- Build Windows Debug: PASS `dotnet build .\NexoraMixStudio.sln -c Debug`.
- Build Windows Release: PASS `dotnet build .\NexoraMixStudio.sln -c Release`.
- Self-test audio/sync/stems/DVS/sampler/DSP: PASS `dotnet run --project .\NexoraMix.SelfTest\NexoraMix.SelfTest.csproj`.
- Self-test preascolto: PASS, il deck produce audio CUE separato dal master.
- Self-test Sync anti-scatti: PASS, render offline continuo durante aggancio e correzione fase.
- Self-test Sync live: PASS, il follower già in play non viene riportato all'inizio.
- Controller tests: PASS `dotnet run --project .\NexoraMix.ControllerTests\NexoraMix.ControllerTests.csproj`.
- JavaScript tablet aggiornato: PASS `node --check NexoraMix.App\RemoteUi\app.js` con runtime Node integrato Codex.
- QR tablet: PASS build WPF con generazione locale tramite QRCoder.
- Avvio WPF: PASS, finestra viva dopo 5 secondi e nessun nuovo errore in `%LOCALAPPDATA%\NexoraMix\Logs\nexora-mix.log`.
- Build Android Debug: PASS `dotnet build .\NexoraMix.Tablet.Android\NexoraMix.Tablet.Android.csproj -c Debug`.
- Build Android Release/APK: PASS `dotnet build .\NexoraMix.Tablet.Android\NexoraMix.Tablet.Android.csproj -c Release`.
- APK Release generato: `NexoraMix.Tablet.Android\bin\Release\net10.0-android\it.nexora.mix.tablet-Signed.apk`.
- Target Android aggiornato a `net10.0-android` per compatibilità con il workload installato.
- Test tablet reale, firewall Windows e latenza Wi-Fi: da verificare su dispositivo fisico.

## Fase 8.15 — Import incrementale cartella Musica

- Aggiunto indice locale dei file gia importati nella cartella Musica dell'utente Windows.
- All'avvio Nexora ripristina in libreria le tracce gia riconosciute senza rianalizzarle e importa solo i file nuovi o modificati.
- Se non ci sono novita nella cartella Musica, non appare nessuna finestra lunga di import e lo stato segnala che non ci sono tracce nuove.
- Quando ci sono tracce nuove, il pannello di processo mostra nome della traccia corrente, conteggio e barra di avanzamento determinata.
- Il watcher della cartella Musica ignora i file gia riconosciuti e importa solo i nuovi arrivi.
- Le tracce gia riconosciute ripristinano BPM, battute e waveform dalla cache senza rieseguire l'analisi audio.

## Verifica 8.15

- Validazione statica: PASS `Validate-Project.ps1`.
- Build Debug: PASS `Build.ps1 -Configuration Debug`.
- Build Release: PASS `Build.ps1 -Configuration Release`.
- Self-test audio/sync/stems/DVS/sampler/DSP: PASS in Debug e Release.
- Controller tests: PASS in Debug e Release.

## Fase 8.16 — Avvio non bloccante

- Spostato il controllo della cartella Musica in background: la finestra principale non deve piu attendere la scansione dei file.
- Anche il pulsante `IMPORTA DALLA CARTELLA` enumera le tracce nuove fuori dal thread dell'interfaccia.
- Il ripristino delle tracce gia note avanza a piccoli blocchi, lasciando respirare l'interfaccia anche con molte tracce.
- Protetto l'indice locale delle tracce per accessi contemporanei tra scansione iniziale e watcher della cartella Musica.
- Corretto un caso limite del sampler offline che poteva uscire dai limiti del buffer durante i self-test.

## Verifica 8.16

- Validazione statica: PASS `Validate-Project.ps1`.
- Build Debug: PASS `Build.ps1 -Configuration Debug`.
- Build Release: PASS `Build.ps1 -Configuration Release`.
- Self-test audio/sync/stems/DVS/sampler/DSP: PASS in Debug e Release.
- Controller tests: PASS in Debug e Release.

## Fase 8.17 — Nora AI DJ Assistant

- Collegata l'app WPF al runtime embedded condiviso di `Nexora.AI` e al catalogo musicale di `PuliziaSpazioDev` tramite riferimenti ai progetti reali, senza copie o dati fittizi.
- Portata la sola app WPF a `net10.0-windows` per consumare i moduli Nexora .NET 10; motore audio, core e test restano sui target esistenti.
- Aggiunta la scheda `NORA AI`, aggiornata automaticamente quando cambia la traccia master/in riproduzione.
- Nora legge `nexora-music.db` in sola lettura e considera tutte le tracce catalogate, incluse quelle non ancora presenti nella libreria di Mix Studio.
- Le tracce vengono classificate per BPM compatibile (anche half-time/double-time), genere e vicinanza dell'anno, con punteggio e motivazione visibili.
- I duplicati della traccia corrente vengono esclusi anche quando hanno identificativi/percorso differenti ma stesso titolo e artista.
- Un suggerimento marcato `PRONTA` può essere caricato direttamente nel primo deck disponibile; le altre tracce restano suggerimenti di catalogo e non simulano un file audio inesistente.
- Il runtime Nora usa lo store condiviso `%LOCALAPPDATA%\Nexora\AI` e mostra anche il numero di insegnamenti musicali condivisi disponibili, senza creare nuova memoria senza consenso.
- Aggiunti test deterministici per ordinamento multi-criterio, penalizzazione di genere/epoca incompatibili e riconoscimento BPM half-time/double-time.

## Verifica 8.17

- Graphify: interrogate le mappe esistenti di `Nexora.AI` e `PuliziaSpazioDev` per identificare runtime embedded, catalogo e contratti riutilizzabili.
- Validazione statica: PASS `Validate-Project.ps1`.
- Build Debug: PASS `Build.ps1 -Configuration Debug`, 0 errori e 0 warning.
- Build Release: PASS `Build.ps1 -Configuration Release`, 0 errori e 0 warning.
- Self-test audio/sync/stems/DVS/sampler/DSP: PASS in Debug e Release.
- Controller test + Nora compatibility: PASS in Debug e Release.
- Avvio WPF Release: PASS, finestra stabile dopo 8 secondi e nessun nuovo errore nel log.
- Controllo visivo Windows: PASS per presenza e apertura della scheda `NORA AI`; verificata connessione reale a 43.214 tracce catalogate e 100 insegnamenti condivisi nell'ambiente corrente.
