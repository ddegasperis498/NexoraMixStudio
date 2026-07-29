# Changelog

## 8.18.0 — Nora DJ Copilot adattiva

- Analisi audio v4 con BPM, Camelot, energia, danceability, stime loudness/peak e cache incrementale.
- Ranking Top 8 multi-fattore, spiegabile, deterministico e consapevole dei dati mancanti.
- Catalogo SQLite indicizzato con FileInstances reali e benchmark da 50.000 tracce.
- Selezione della traccia realmente udibile tramite stato deck, meter, master e crossfader.
- Personalizzazione locale persistente con feedback, decay, profili, reset ed export/import.
- Memoria persistente del set, fasi energetiche, planner di tre tracce e alternative.
- Transition advisor con deck, pitch, cue, durata, bass swap, rischio e confidenza.
- Preview isolata sul bus cuffia e caricamento senza play/sync/master automatici.
- UI Nora professionale con piano, dettagli, controlli sessione e feedback.
- Rimosse le dipendenze assolute dai repository sibling; aggiunti SDK pin, CI Windows e publish win-x64.
- Aggiornato SQLite nativo alla versione 2.1.12 per rimuovere la segnalazione NU1903.

## 8.0.0 — Tablet Console Edition

- Interfaccia Windows ridisegnata in schede chiare.
- Console principale con due piatti grandi e mixer centrale.
- Vista quattro deck separata.
- Mixer 4 canali completo e leggibile.
- Loop, hot cue ed effetti raggruppati per funzione.
- Aumento dimensione testi, pulsanti e righe libreria.
- Server locale ASP.NET Core integrato nel processo WPF.
- Comunicazione tablet/PC tramite WebSocket.
- Codice temporaneo di abbinamento.
- Console touch responsive per browser Android.
- Piatti animati in tempo reale durante la riproduzione.
- Touch jog per nudge/seek.
- Waveform e playhead remoti.
- Controllo di pitch, BPM, Sync, loop, cue, effetti, EQ, fader e crossfader.
- Wrapper Android `.NET 8` separato.
- Cartella Musica/Inbox monitorata automaticamente.
- Importazione YouTube limitata all'apertura sicura del link; nessun downloader/ripping integrato.
