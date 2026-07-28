# Audio engine V7

Pipeline per deck:

```text
AudioFileReader
 -> stereo/sample-rate conversion
 -> TempoSampleProvider
 -> ThreeBandEqSampleProvider
 -> DeckFxSampleProvider
 -> deck gain
 -> crossfader gain
```

Pipeline master:

```text
Deck A/B/C/D
 -> MixingSampleProvider
 -> master gain
 -> limiter -1 dBFS
 -> recording tap
 -> shared audio clock
 -> single WaveOutEvent
```

Verifica sorgente del 2026-07-03: il codice attivo contiene una sola creazione `WaveOutEvent`, in `MasterAudioEngine`. Il vecchio `DeckPlayer` per-deck e stato rimosso per impedire uscite audio indipendenti.

Il tempo provider interno cambia velocità e intonazione. Il key-lock professionale resta da integrare.
