# DSP effects

## Implementato e verificato in self-test

- DJ filter: low-pass verso sinistra, high-pass verso destra.
- Echo: delay stereo con feedback controllato.
- Bit crusher: quantizzazione progressiva.
- Saturation: drive non lineare con `tanh`.
- Gate: attenuazione dei segnali sotto soglia.
- Compressor: compressione soft sopra soglia.
- Roll: ripetizione buffer breve con finestra variabile.
- Brake: attenuazione progressiva tipo tape/vinyl brake.

## Collegamento UI

Ogni deck espone controlli reali per:

- FILTER
- ECHO
- CRUSH
- SAT
- GATE
- COMP
- ROLL
- BRAKE

I controlli scrivono nel `DeckChannel` del deck e vengono applicati nella pipeline audio prima del gain/crossfader.

## Non ancora implementato

Reverb, flanger, phaser, chorus, beat repeat, vinyl start, backspin, tape stop, pitch echo, trans gate, noise sweep, low cut echo, high cut echo, loop roll, stutter, pan, auto pan e LFO filter restano da implementare.
