# Stems engine

## Implementato

Il progetto supporta bundle di stems locali pre-separati:

- Vocals
- Drums
- Bass
- Other

Ogni stem ha percorso file, gain, mute e solo. Il planner valida file mancanti, duplicati e range gain.

## Regola anti-fake

Non viene eseguita separazione automatica degli stems. Se l'utente non fornisce file stem reali gia separati, il software non dichiara stems disponibili.

## Verifiche automatiche

- Bundle locale pre-separato valido: PASS.
- Solo/mute rispettati dal planner: PASS.

## Limiti

- Separazione offline AI non implementata.
- Cache stems non implementata.
- UI stem mute/solo non ancora implementata.
