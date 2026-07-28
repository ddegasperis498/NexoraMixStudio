# Controller MIDI e console

## Implementato

Il progetto usa `NAudio.Midi` per:

- enumerare ingressi e uscite MIDI;
- collegare un dispositivo;
- ricevere Note, CC, Pitch Bend, Program Change e Aftertouch;
- inviare Note e Control Change per feedback generico;
- usare più deck attraverso canali e mapping distinti;
- scollegare il controller senza arrestare il motore audio.
- validare profili controller personalizzati;
- salvare e ricaricare profili controller JSON;
- descrivere soft takeover, dead zone, sensibilità, layer e modifier nel modello mapping.

## Profili

La V7 include un profilo **Generic MIDI 4 Deck** con play/cue/sync/master per A/B/C/D, volumi, crossfader, master gain, pitch fader e feedback MIDI generico. I nomi commerciali del dispositivo vengono mostrati, ma il produttore non implica compatibilità completa.

| Famiglia | Stato |
|---|---|
| Controller MIDI generici | Implementato, da mappare |
| Pioneer DJ / AlphaTheta DDJ | Generic MIDI / non verificato per modello |
| CDJ / XDJ | MIDI controller mode possibile, non verificato |
| Gemini | Generic MIDI / non verificato per modello |
| Denon DJ | Generic MIDI / non verificato per modello |
| Numark | Generic MIDI / non verificato per modello |
| Hercules | Generic MIDI / non verificato per modello |
| Reloop | Generic MIDI / non verificato per modello |
| Traktor Kontrol | Generic MIDI parziale / non verificato |
| HID proprietario e display | Non implementato |

## Verifiche automatiche

- Fallback Generic MIDI: PASS.
- Mapping completo 4 deck: PASS.
- Pitch fader per quattro deck: PASS.
- Soft takeover nel modello mapping: PASS.
- Feedback MIDI generico: PASS.
- Import/export profilo controller JSON: PASS.
- Profili vendor marcati sperimentali: PASS.

## Sicurezza

I messaggi sconosciuti vengono mostrati come diagnostica e non eseguono script o comandi esterni.
