# Agente 02 — Audio engine

## Implementato
- una sola istanza `WaveOutEvent`;
- `MixingSampleProvider` condiviso;
- due `DeckChannel` sullo stesso clock;
- conversione stereo e sample-rate comune;
- crossfader equal-power;
- gain, EQ a tre bande, limiter e meter;
- start schedulato sul frame master;
- dispose e sostituzione traccia.

## Limite noto
Il provider tempo incluso usa interpolazione/resampling: sincronizza il tempo, ma cambia l'intonazione. `ITimeStretchProcessor` rende sostituibile il componente; key-lock professionale non dichiarato come completato.
