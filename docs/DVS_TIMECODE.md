# DVS / timecode

## Implementato

- Decoder timecode sintetico a quadratura.
- Rilevamento assenza segnale.
- Rilevamento direzione forward.
- Rilevamento direzione reverse.
- Misura energia segnale e velocita relativa.

## Stato reale

Questa e una base di architettura DVS/timecode, non un DVS completo per vinile/CDJ. Non controlla ancora deck esterni reali e non dichiara compatibilita con timecode commerciali.

## Verifiche automatiche

- No signal: PASS.
- Forward sintetico: PASS.
- Reverse sintetico: PASS.

## Non verificato

- Input audio reale da giradischi/CDJ.
- Timecode Serato/Traktor/Rekordbox o altri formati proprietari.
- Latenza e stabilita scratch reale.
