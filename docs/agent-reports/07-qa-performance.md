# Agente 07 — QA e prestazioni

## Self-test incluso
- curva equal-power;
- tempo ratio;
- limite tempo;
- quantizzazione alla misura;
- errore di fase;
- BPM delle due tracce demo.

## Controlli statici
- XML/XAML/csproj: PASS;
- delimitatori C#: PASS;
- una sola creazione `WaveOutEvent`: PASS;
- player legacy assente: PASS.

## Non verificato
Build Debug/Release, audio hardware, stress test e metriche CPU: richiedono Windows con .NET 8 e dispositivo audio.
