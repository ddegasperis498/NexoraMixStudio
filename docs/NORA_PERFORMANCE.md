# Prestazioni Nora

## Strategia

- filtro SQLite indicizzato prima del ranking;
- pool massimo di 1.000 candidati;
- query BPM normale, half-time e double-time;
- cache breve delle query equivalenti;
- debounce UI di 200 ms;
- cancellazione e numero di generazione contro risultati obsoleti;
- analisi audio e scansione libreria fuori dal thread grafico;
- beam search limitata per il piano di tre tracce.

## Misure riproducibili

Il controller test costruisce un database SQLite reale con 50.000 tracce e verifica che il pool rimanga limitato. Sul computer di sviluppo la query misurata nelle esecuzioni finali è risultata circa 38 ms (pool 1.979, 1.000 risultati), contro circa 285 ms della precedente scansione completa osservata in baseline. Il target informativo è inferiore a 500 ms; non è usato come test fragile di successo/fallimento.

## Metriche esposte

`NoraCatalogMetrics` pubblica durata query, numero di candidati, risultati, cache hit e dimensione catalogo. Lo stato UI include catalogo, suggerimenti, fase/piano e motivo della scelta del deck.

## Interpretazione

I tempi dipendono da disco, cache del sistema operativo, dimensione del database e antivirus. Il test prestazionale dimostra l'ordine di grandezza e soprattutto impedisce la regressione architetturale a un caricamento completo in memoria.
