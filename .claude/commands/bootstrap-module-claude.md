# Command: /bootstrap-module-claude

## Usage
```
/bootstrap-module-claude <ModuleName> [--vision "<description courte>"] [--depends-on <Module1,Module2>]
```

## Description
Génère le `.claude/` complet d'un nouveau module MicroKit en posant les bonnes questions
et en produisant tous les fichiers (CLAUDE.md, settings.json, agents, commands, hooks, rules, skills).

Équivalent de ce qui a été fait pour MicroKit.Result et MicroKit.MediatR.

## Exemples
```
/bootstrap-module-claude Domain --vision "Primitives DDD : AggregateRoot, Entity, ValueObject, DomainEvent"
/bootstrap-module-claude Caching --vision "Cache distribué multi-couches avec Result<T>" --depends-on Result
/bootstrap-module-claude Messaging --vision "Bus de messages avec outbox pattern et saga" --depends-on Result,Domain
```

## Process

### Étape 1 — Questions posées à l'utilisateur
```
1. Relation avec une lib tierce ? (Wrapper / Abstraction pure / Implémentation custom)
2. Patterns principaux couverts ?
3. Intégration MicroKit.Result ? (Optionnelle / Obligatoire / Non)
4. Providers prévus ? (ex: EF Core, Dapper, Redis, RabbitMQ...)
5. Behaviors / middlewares ? (pipeline propre au module ?)
6. Testabilité : helpers de test fournis ?
```

### Étape 2 — Génération des fichiers .claude/

Fichiers générés dans `modules/MicroKit.[ModuleName]/.claude/` :
```
CLAUDE.md           ← vision, architecture, patterns, conventions spécifiques
settings.json       ← pré-configuré avec le nom du module, dépendances, agents
agents/
  [module]-architect.md       ← décisions architecturales du module
  [module]-reviewer.md        ← review spécialisée
  test-generator.md           ← génération de tests spécifiques
commands/
  new-[primary-concept].md    ← commande principale du module
  gen-tests.md
  review-perf.md
hooks/
  validate-[module]-contract.md
rules/
  [module]-patterns.md        ← patterns autorisés/interdits
  csharp-style.md             ← style spécifique si nécessaire
  no-[antipattern].md
skills/
  [module]-internals.md       ← fonctionnement interne
  [module]-testing.md         ← patterns de test
  [provider]-integration.md   ← par provider si applicable
```

### Étape 3 — Enregistrement
Met à jour `.claude/settings.json` moduleRegistry avec le nouveau module.

## Notes
Ce command est le point d'entrée standard pour tout nouveau module.
Ne jamais commencer à implémenter un module sans avoir son `.claude/` complet.
