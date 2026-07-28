# Remix engine

## Implementato

- Sampler audio nello stesso master mixer.
- Caricamento sample locali in slot.
- Trigger one-shot con velocity.
- Choke per slot.
- Stop di tutte le voci sampler.
- Step sequencer a 16 step, configurabile fino a 64 step.
- Sequencer sincronizzato al master clock audio.

## Verifiche automatiche

- Caricamento sample locale: PASS.
- Trigger sample: PASS.
- Audio sampler nel master offline: PASS.
- Step sequencer su clock master: PASS.

## Limiti

- UI sampler dedicata non ancora implementata.
- Pad modes avanzati, scene, snapshot e undo/redo non ancora implementati.
