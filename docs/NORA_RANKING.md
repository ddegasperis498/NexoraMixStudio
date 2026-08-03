# Ranking Nora

Il ranking è deterministico, spiegabile e contestuale. Ogni candidato riceve fattori strutturati; la somma dei contributi coincide con il punteggio finale mostrato.

## Fattori

- compatibilità BPM, inclusi half-time e double-time;
- pitch necessario e penalità per correzioni eccessive;
- relazione Camelot: stessa tonalità, adiacente, relativa o incompatibile;
- energia corrente e traiettoria target;
- genere e vicinanza temporale;
- combinazione vocale/strumentale quando nota;
- disponibilità fisica del file;
- qualità della transizione;
- preferenze apprese;
- traccia/artista recente e affaticamento;
- penalità d'incertezza per dati insufficienti.

I fattori mancanti sono esclusi dal denominatore e producono una penalità d'incertezza esplicita: non diventano falsi zeri. I pareggi sono risolti stabilmente per identificativo.

## Pool candidati

SQLite esegue prima il filtro indicizzato e restituisce al massimo 1.000 candidati. Il codice deduplica titolo/artista, esclude la traccia corrente e risolve il miglior file realmente esistente. Solo dopo applica il ranking completo e restituisce Top 8.

## Versioni

- feature audio: 4;
- pesi ranking: 2.

Le spiegazioni sono adatte alla UI ma conservano anche nome, valore grezzo, peso e contributo per diagnosi e test.
