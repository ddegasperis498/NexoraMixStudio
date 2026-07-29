# Analisi audio Nora

L'analisi riusa una sola lettura PCM e produce `TrackAudioFeatures` versione 4. Le feature sono persistite nella cache di import e, per i file locali collegati al catalogo, in `TechnicalAudioFeatures` come JSON versionato.

## Feature disponibili

- BPM, confidenza, beat grid e downbeat stimato;
- tonalità stimata, modo e codice Camelot;
- energia e danceability normalizzate;
- centroide spettrale, intensità delle basse e transiente;
- loudness integrata stimata;
- sample peak e true peak stimato;
- stato, versione algoritmo, data, errore e confidenza complessiva.

## Stati e valori mancanti

Gli stati sono `Unknown`, `Pending`, `Partial`, `Complete` e `Failed`. I valori non misurati rimangono nullable o `Unknown`: non vengono convertiti in zero. Voce e sezioni strutturali restano attualmente sconosciute salvo dati reali già disponibili; ranking e transition advisor usano un fallback conservativo.

## Precisione dichiarata

Loudness e true peak sono stime DSP utili al ranking, non misure certificate EBU R128/ITU-R BS.1770. La chiave è una stima cromatica e può risultare `Unknown` con segnale poco tonale. La UI usa “—” quando il dato manca.

## Cache e invalidazione

Una voce è riusabile solo se percorso, dimensione, timestamp e versione dell'algoritmo coincidono. File modificati, analisi fallite/sconosciute e versioni precedenti vengono rianalizzati. Il test di restart usa file temporanei reali e verifica conservazione e invalidazione.
