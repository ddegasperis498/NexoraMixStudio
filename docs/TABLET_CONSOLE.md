# Console Tablet

## Flusso

```text
Tablet/Browser
  -> WebSocket autenticato con codice temporaneo
  -> TabletRemoteHost nel processo WPF
  -> MainViewModel sul Dispatcher WPF
  -> MasterAudioEngine
  -> uscita audio Windows
```

## Stato inviato

Ogni snapshot contiene:

- master deck;
- stato globale;
- peak e clipping;
- stato registrazione/mashup;
- quattro deck;
- titolo e artista;
- BPM ed effective BPM;
- posizione e durata;
- volume e meter;
- loop;
- EQ;
- filtro ed effetti;
- battuta e misura;
- waveform ridotta.

## Comandi

- Play/Pausa;
- Cue e Set Cue;
- Stop;
- Sync e Master;
- loop size e loop toggle;
- hot cue;
- BPM target e pitch;
- volume/EQ/filter/effect;
- seek e jog relativo;
- crossfader;
- Auto Mashup;
- recording;
- Stop All.

## Sicurezza

- codice casuale a sei cifre generato a ogni avvio;
- query token o header `X-Nexora-Pairing`;
- server limitato alla rete locale dall'utente/firewall;
- nessuna esposizione automatica su Internet;
- nessun caricamento remoto di file nel prototipo.

## Animazione piatti

La pagina interpola localmente la posizione tra due snapshot e aggiorna il marker con `requestAnimationFrame`. Lo stato viene corretto a ogni snapshot del PC, evitando che l'animazione grafica diventi la sorgente del clock audio.
