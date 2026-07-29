# Transition Advisor Nora

Per ogni suggerimento Nora prepara una ricetta di mix, non un'automazione.

## Output

- deck consigliato;
- pitch percentuale e indicazione sync;
- punto di uscita e ingresso in tempo e battute;
- sezione quando disponibile;
- durata fra 8, 16, 32 o 64 beat;
- battuta del bass swap;
- consiglio sulla sovrapposizione vocale;
- rischio e confidenza.

Il cue viene sempre limitato alla durata della traccia. Il deck suggerito rispetta la policy del deck realmente libero e la posizione del crossfader.

## Fallback

Quando sezioni o voce sono sconosciute, l'advisor usa una transizione conservativa e riduce la confidenza. Due voci predominanti aumentano il rischio; una voce verso una parte strumentale è favorita.

`RequiresExplicitDjAction` è sempre vero: Nora non esegue play, sync, bass swap o spostamenti del crossfader.
