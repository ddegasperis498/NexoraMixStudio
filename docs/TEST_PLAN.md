# Test plan V7

## Automatici inclusi

`NexoraMix.SelfTest`:
- equal-power crossfader;
- tempo ratio;
- quantizzazione;
- fase;
- BPM demo e sintetico;
- conteggio battute;
- AutoMashupPlanner.

`NexoraMix.ControllerTests`:
- fallback Generic MIDI;
- catalogo mapping 4 deck;
- riconoscimento famiglie Pioneer/AlphaTheta, Gemini, Denon, Numark e Hercules;
- stato vendor sperimentale.

## Manuali Windows obbligatori

1. caricare quattro WAV/MP3;
2. avviarli simultaneamente;
3. verificare che fermare un deck non fermi gli altri;
4. provare Sync su B/C/D rispetto ad A;
5. eseguire Auto Mashup;
6. registrare un WAV;
7. verificare clipping e peak;
8. collegare un controller MIDI reale;
9. testare play/cue/fader/pitch;
10. provare scaling 100/125/150/175%.
