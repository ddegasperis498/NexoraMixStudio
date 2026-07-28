# Nexora Mix Tablet Android

Wrapper Android nativo basato su WebView per la console remota servita dal programma Windows.

## Requisiti

- Visual Studio con workload **Sviluppo di app mobili con .NET**;
- .NET 8 Android workload;
- tablet Android 8.0 (API 26) o successivo;
- PC e tablet sulla stessa rete Wi-Fi.

## Compilazione

Aprire `NexoraMix.Tablet.Android.sln`, selezionare un dispositivo/emulatore Android e avviare.

## Collegamento

1. Sul PC avviare Nexora Mix Studio.
2. Aprire la scheda **Tablet / Android**.
3. Premere **Avvia console tablet**.
4. Nell'app Android inserire l'indirizzo visualizzato, ad esempio `http://192.168.1.20:17840/`.
5. Nella pagina inserire il codice di abbinamento a 6 cifre.

L'audio resta sul PC. Il tablet invia comandi e riceve lo stato dei quattro deck tramite WebSocket.
