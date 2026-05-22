# Hook: Validate Behavior Order

## Déclenchement
Après chaque création ou modification d'un behavior ou de `PipelineOrder.cs`.

## Vérifications

### Ordre dans PipelineOrder.cs
```
Valeurs attendues (non modifiables sans approbation) :
  Logging       = 100
  Authorization = 200
  Validation    = 300
  Idempotency   = 400
  Caching       = 500
  Retry         = 600

Règles :
- Aucun doublon de valeur
- Nouveau behavior entre 101-599 (jamais < 100, jamais > 600 sauf extension explicite)
- Behaviors custom documentés dans PipelineOrder.cs avec un commentaire de justification
```

### Scope correct
```
IdempotencyBehavior → ne s'applique qu'aux ICommand (pas IQuery) ?
CachingBehavior     → ne s'applique qu'aux IQuery (pas ICommand) ?
Tous les behaviors  → guard "if (request is not IMarker) return await next()" présent ?
```

### Enregistrement DI cohérent
```
Behavior enregistré comme typeof(IPipelineBehavior<,>) (open generic) ?
Ordre d'enregistrement dans ServiceCollection = ordre PipelineOrder ?
(MediatR exécute les behaviors dans l'ordre d'enregistrement DI)
```

### Pas d'appel re-entrant
```
Behavior qui appelle _mediator.Send() ou _mediator.Publish() → ❌ BLOQUANT
Risque : boucle infinie si le même request entre dans son propre pipeline
```

## Format de sortie

```
✅ Pipeline order valid:
   100 LoggingBehavior
   150 AuditBehavior        ← custom, between 100-200 ✓
   200 AuthorizationBehavior
   300 ValidationBehavior
   400 IdempotencyBehavior  (commands only ✓)
   500 CachingBehavior      (queries only ✓)
   600 RetryBehavior
   No duplicates. No gaps > 100.

❌ Pipeline violations:
   RetryBehavior registered at order 200 in DI, expected 600
   → Fix: reorder AddRetryBehavior() after AddValidationBehavior() in DI registration
```
