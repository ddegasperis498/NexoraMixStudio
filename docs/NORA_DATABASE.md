# Database Nora

## Catalogo musicale

`NoraCatalogRepository` usa `Microsoft.Data.Sqlite`, query parametrizzate, pooling e WAL già configurato dal catalogo. Valida lo schema esistente e non ricrea le tabelle di base. Prima delle sole migrazioni additive crea un backup best-effort.

Indici aggiunti in transazione:

- `IX_Tracks_Bpm`;
- `IX_Tracks_UpdatedAtUtc`;
- `IX_FileInstances_AvailableTrack`.

`FileInstances` è la fonte autorevole per la disponibilità: il resolver accetta soltanto estensioni audio supportate e file fisicamente esistenti. `TechnicalAudioFeatures` conserva il JSON v4. L'upsert è incrementale e non cancella dati di altri processi.

## Store Nora

Personalizzazione (`user_version=1`):

- `NoraFeedbackEvents`;
- `NoraDjPreferenceProfiles`;
- `NoraLearnedWeights`;
- `NoraArtistPreferences`;
- `NoraCombinationPreferences`.

Memoria set (`user_version=1`):

- `SetSession`;
- `SetSessionTrack`;
- `SetTransition`;
- `SetRecommendation`;
- `SetFeedback`.

Tutte le scritture correlate sono transazionali. Le sessioni chiuse restano nello storico, mentre una nuova sessione non eredita la lista “recenti” della precedente.

## Compatibilità

Il database del catalogo resta compatibile con il produttore esterno: Nora aggiunge soltanto indici e righe relative a file/feature che ha realmente osservato. Se il catalogo non esiste o non è valido, la UI espone l'errore senza inventare tracce.
