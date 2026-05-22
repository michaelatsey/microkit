# Command: /gen-handler-tests

## Usage
```
/gen-handler-tests <HandlerFilePath> [--coverage <unit|integration|full>] [--harness]
```

## Description
Analyse un handler existant et génère les tests manquants via le bon harness.
Détecte automatiquement le type (Command/Query/Event/Stream) et adapte le template.

## Exemples
```
/gen-handler-tests src/.../CreateOrderHandler.cs
/gen-handler-tests src/.../GetUserByIdHandler.cs --coverage full
/gen-handler-tests src/.../UserRegisteredHandler.cs --harness
```

## Process

1. **Détecter le type de handler** — via l'interface implémentée
   - `ICommandHandler` → `CommandHandlerTestHarness`
   - `IQueryHandler` → `QueryHandlerTestHarness`
   - `IDomainEventHandler` → `DomainEventTestHarness`
   - `IStreamQueryHandler` → test avec `IAsyncEnumerable` collector

2. **Analyser les dépendances** — constructeur du handler
   - Générer les mocks NSubstitute correspondants

3. **Inférer les cas de test** — depuis la signature et le corps
   - Success path
   - Failure paths (null, not found, business rule)
   - CancellationToken propagé
   - Domain events publiés
   - Idempotency si applicable

4. **Générer** les tests dans le projet de test correspondant

## Cas inférés automatiquement

| Condition dans le handler | Test généré |
|---|---|
| `if (x is null) return Failure(NotFoundError)` | `ReturnNotFoundError_WhenEntityDoesNotExist` |
| `if (!user.IsActive)` | `ReturnFailure_WhenUserIsInactive` |
| `_events.Publish(new XEvent(...))` | `PublishXEvent_WhenSuccessful` |
| `Result.Try(() => ...)` | `ReturnTechnicalError_WhenInfrastructureThrows` |
| `ct.ThrowIfCancellationRequested()` | `ThrowOperationCancelled_WhenCancelled` |
| Retour `IAsyncEnumerable` | `YieldAllItems_WhenStreamHasData` + `YieldNothing_WhenStreamEmpty` |

## Output format

```
✅ Generated: tests/MicroKit.MediatR.Tests/Commands/CreateOrderHandlerTests.cs
   - 6 test cases inferred
   - 2 mocks: IOrderRepository, IDomainEventDispatcher
   - Harness: CommandHandlerTestHarness<CreateOrderCommand, Result<OrderId>>
   
⚠️  Manual review needed:
   - Line 42: complex business logic — add edge case tests manually
   - Line 67: external HTTP call — consider adding retry scenario
```
