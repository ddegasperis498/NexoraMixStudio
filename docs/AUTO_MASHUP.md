# Auto Mashup

Il planner accetta da due a quattro file locali analizzati.

## Processo

1. sceglie il master in base a confidenza BPM/fase e durata;
2. verifica la variazione tempo massima;
3. mette il master scelto al primo ingresso del piano;
4. assegna i deck A–D;
5. assegna ruoli musicali;
6. imposta gain ed EQ iniziali;
7. avvia il master;
8. programma i follower su misure successive;
9. mantiene Sync e correzione della fase quando la beat-grid è affidabile;
10. protegge il bus master con limiter.

## Verifiche automatiche

- Piano a 2 deck: PASS.
- Piano a 4 deck: PASS.
- Master scelto al primo ingresso: PASS.
- Ingresso immediato del master: PASS.
- Ruolo vocale in modalità Balanced a 4 deck: PASS.

## Modalità

- Safe: foundation, support, texture, percussion.
- Balanced: foundation, support, vocals, percussion.
- Creative: foundation, bass, vocals, texture.
- Aggressive: full mix, drums, vocals, FX layer.

## Limiti

Il ruolo è una strategia di mix, non una separazione stems. Senza stems reali, ogni deck riproduce il file completo filtrato ed equalizzato.
