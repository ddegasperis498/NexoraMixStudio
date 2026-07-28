# Sync engine

## Implementato

- Calcolo tempo ratio tra master e follower.
- Rifiuto automatico quando la differenza BPM supera il limite configurato.
- Quantizzazione a battuta.
- Quantizzazione a misura.
- Quantizzazione a frase.
- Correzione fase durante la riproduzione quando la fase e affidabile.
- Fallback BPM-only quando la fase non e affidabile.
- Auto Mashup con follower armati su frase e ingressi progressivi in misure.

## Parametri

- `BeatsPerBar`: default 4.
- `PhraseLengthBars`: default 4.
- `MaximumSyncPercent`: configurabile dall'app, massimo 25%.

## Verifiche automatiche

- Calcolo ratio 124 -> 118 BPM: PASS.
- Rifiuto ratio oltre limite: PASS.
- Quantizzazione battuta: PASS.
- Quantizzazione misura: PASS.
- Quantizzazione frase: PASS.
- Errore fase in millisecondi: PASS.

## Limiti

- Key-lock professionale non implementato.
- Analisi tonalita/key non implementata.
- Drift e stabilita con hardware audio reale: NON VERIFICATO.
