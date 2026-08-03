# Personalizzazione Nora

Nora apprende localmente dalle azioni del DJ senza addestrare un modello remoto. Gli eventi espliciti e impliciti aggiornano preferenze limitate e spiegabili.

## Feedback

Sono supportati, fra gli altri: eccellente, non adatta, troppo energetica, troppo lenta, artista da evitare, caricata, suonata, ignorata e saltata. La UI espone le azioni principali; gli eventi automatici sono registrati solo quando realmente osservati.

## Protezioni anti-overfitting

- profilo neutro di partenza;
- learning rate configurabile;
- soglia minima di osservazioni;
- decay temporale;
- clamp dei pesi;
- confidenza dell'evento;
- profili DJ separati;
- possibilità di disattivare l'apprendimento.

Il profilo predefinito può reagire già al primo feedback esplicito; profili personalizzati possono alzare la soglia.

## Persistenza e controllo

Lo store SQLite contiene eventi, profili, pesi appresi, preferenze artista e combinazioni. Sono disponibili reset, export e import. Il test end-to-end verifica che un feedback negativo cambi realmente l'ordine e che l'effetto resti dopo la riapertura del database.

Nora non acquisisce dati da Internet e non condivide preferenze senza un'azione esplicita esterna all'app.
