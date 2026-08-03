# Set Planner Nora

Il planner non sceglie soltanto la prossima traccia: costruisce un percorso di tre passi con alternative, rispettando la fase e la traiettoria energetica del set.

## Fasi

Warm-up, crescita progressiva, mantenimento, peak time, secondo picco, pausa controllata, ripresa, chiusura, after party e modalità libera.

## Ricerca

La ricerca usa beam search con ampiezza limitata. A ogni passo applica il ranking completo, esclude duplicati e tracce recenti, richiede un file disponibile, limita aumento BPM e calo energetico e conserva le migliori alternative. Input identici producono lo stesso piano.

Il planner restituisce un errore esplicito se non esistono almeno tre candidati eseguibili; non completa il piano con dati finti.

## Memoria del set

`NoraSetMemoryStore` distingue tracce caricate e realmente suonate, raccomandazioni, transizioni e feedback. Pausa/ripresa sopravvivono al riavvio. Le sessioni chiuse restano consultabili ma non contaminano il contesto recente del nuovo set.
