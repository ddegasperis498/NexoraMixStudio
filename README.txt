HOTFIX NEXORA MIX STUDIO V5.3 HYBRID

Questo hotfix abilita:
- caricamento delle singole tracce Spotify come deck esterno;
- Play/Pausa sul dispositivo Spotify attivo;
- BPM manuale con step da 0,1;
- tempo locale fino a ±25%;
- Sync che avvia automaticamente il master;
- fallback di sincronizzazione BPM quando la fase non è affidabile.

Applicazione:

Set-ExecutionPolicy -Scope Process Bypass -Force
.\Apply-NexoraMix-v5.3.ps1 -ProjectRoot "C:\Users\domen\Qsync\Progetti\AreaSviluppo\NexoraMixStudio"

Dopo l'hotfix devi riconnettere Spotify per autorizzare i nuovi permessi di riproduzione.
