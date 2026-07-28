# Agente 06 — Spotify, libreria e persistenza

## Implementato
- OAuth PKCE esistente conservato;
- import di metadati track/album/playlist;
- messaggi espliciti: Spotify non fornisce audio al mixer;
- pulsanti deck disabilitati senza file locale;
- mapping Spotify ID → file locale;
- ripristino automatico del mapping;
- sessioni e impostazioni con scrittura atomica e backup.

## Vincolo
Nessun download, cattura, decodifica o aggiramento DRM Spotify.
