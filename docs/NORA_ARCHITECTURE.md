# Architettura Nora DJ Copilot

Nora è un copilota locale integrato in Nexora Mix Studio. Osserva lo stato dei deck, individua la traccia realmente udibile, interroga un catalogo SQLite con un pool limitato, ordina i candidati e propone un piano eseguibile. Non avvia mai una traccia e non modifica automaticamente master, sync o crossfader.

## Flusso

1. `NoraDeckStateProvider` calcola deck corrente, deck in uscita, cue e deck libero usando play, volume, meter, master e crossfader.
2. `NoraCatalogRepository` aggiorna le istanze dei file locali e prefiltra il catalogo per BPM normale, half-time/double-time, genere e anno.
3. `NoraTrackCompatibility` combina BPM/pitch, Camelot, energia, genere, epoca, voce, disponibilità, transizione, memoria recente e preferenze del DJ.
4. `NoraSetPlanner` cerca tre passi futuri con beam search limitata.
5. `NoraTransitionAdvisor` produce deck, pitch, cue, durata, bass swap, rischio e confidenza.
6. `MainViewModel.Nora` applica debounce e cancellazione, pubblica Top 8 e piano, registra soltanto eventi reali.

## Confini di responsabilità

- `NexoraMix.Audio`: decodifica, analisi PCM e canale preview cuffia.
- `NexoraMix.Core`: modelli, ranking, apprendimento, set planner, transizioni e policy deck.
- `NexoraMix.App`: repository SQLite, store locali, orchestrazione e WPF.
- `NexoraMix.SelfTest`: DSP, cache e isolamento master/cue.
- `NexoraMix.ControllerTests`: ranking, planner, database, personalizzazione e 50.000 tracce.

## Sicurezza live

Il PC resta la sorgente del clock audio. Il caricamento consigliato usa esclusivamente un deck completamente vuoto e non esegue play. La preview viene miscelata soltanto nel bus cue. Ogni consiglio di transizione richiede un'azione esplicita del DJ.

## Dati locali

- catalogo condiviso letto se presente: `%LOCALAPPDATA%\Nexora\PuliziaSpazioDev\Data\nexora-music.db`;
- preferenze: `%LOCALAPPDATA%\NexoraMix\Nora\nora-personalization.db`;
- memoria set: `%LOCALAPPDATA%\NexoraMix\Nora\nora-set-memory.db`;
- cache e indice import: dati applicativi locali già usati da Mix Studio.

Il repository è autonomo in compilazione: non contiene riferimenti di progetto alle cartelle sorelle `Nexora.AI` o `PuliziaSpazioDev`.
