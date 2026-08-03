# Rapporto test Nora — 2026-07-29

## Esito

- validazione statica: PASS;
- build Debug: PASS, 0 errori e 0 warning;
- build Release: PASS, 0 errori e 0 warning;
- SelfTest Debug/Release: PASS;
- ControllerTests Debug/Release: PASS;
- publish Release win-x64 framework-dependent: PASS;
- avvio e controllo visivo WPF Release: PASS;
- `git diff --check`: PASS;
- riferimenti progetto esterni: 0;
- percorsi `C:\Users` nei file progetto: 0.

## Copertura significativa

- BPM sintetici 90, 100, 118, 120, 124, 128 e 140 entro ±0,55 BPM;
- feature v4, cache reale, restart e invalidazione file/versione;
- Camelot, pitch, energia, voce, uncertainty, disponibilità e tie-break;
- selezione deck in sette configurazioni di crossfader/master/cue;
- preview udibile nel cue, master offline silenzioso e deck inattivi;
- SQLite reale: migrazione, FileInstances, feature JSON, resolver e cancellazione;
- catalogo sintetico reale da 50.000 tracce con pool limitato;
- feedback che cambia l'ordine e persiste dopo riapertura;
- lifecycle set completo, isolamento e memoria recente;
- planner deterministico e fallback candidati insufficienti;
- transition advisor con sezioni note e fallback Unknown.

La prova visiva ha confermato finestra stabile e scheda Nora leggibile con stato sessione, piano adattivo, colonne Top 8 e azioni DJ. Non sono stati attivati master, play o preview durante il controllo.

## Prestazioni osservate

Nell'ultima esecuzione la query su 50.000 tracce ha impiegato circa 36 ms e restituito 1.000 elementi da un pool di 1.979. Il dato è informativo e dipende dalla macchina.

## Verifiche non equivalenti a hardware reale

Non sono state certificate in questa fase la qualità musicale su un dataset annotato, la misura EBU R128, una scheda cue fisica, controller fisici, latenza Wi-Fi/tablet o un dispositivo Android. Queste verifiche richiedono hardware e ascolto umano dedicati.
