# Hook: Validate Domain Purity

## Déclenchement
Après chaque génération ou modification d'un fichier dans src/MicroKit.Domain/

## Vérifications

```
Pour chaque fichier .cs modifié dans src/ :

1. Scanner les using statements
   → Présence de MicroKit.* → ❌ BLOQUANT
   → Présence de EntityFramework, MediatR, Serilog → ❌ BLOQUANT
   → Présence de Microsoft.AspNetCore.* → ❌ BLOQUANT

2. Scanner les injections de constructeur
   → ILogger<T> → ❌ BLOQUANT
   → IMediator → ❌ BLOQUANT
   → IServiceProvider → ❌ BLOQUANT
   → DbContext → ❌ BLOQUANT

3. Scanner les méthodes async
   → async dans une méthode de domaine → 🟡 WARNING (justification requise)

4. Scanner les types d'exceptions
   → throw new HttpRequestException → ❌ BLOQUANT
   → throw new SqlException → ❌ BLOQUANT
   → throw new Exception → 🟡 WARNING (utiliser DomainException)
```

## Format de sortie
```
✅ Domain purity check passed: Order.cs
❌ Domain purity violation: OrderService.cs
   Line 12: using MicroKit.Result; → remove this dependency
   Line 34: ILogger<OrderService> logger → remove from constructor
```

---

# Hook: Validate Domain Event

## Déclenchement
Après chaque génération ou modification d'un DomainEvent.

## Vérifications

```
1. Est un sealed record ? → sinon ❌ BLOQUANT
2. Hérite de DomainEvent ? → sinon ❌ BLOQUANT
3. Contient OccurredAt : DateTimeOffset ? → sinon ❌ BLOQUANT
4. Contient l'ID de l'agrégat concerné ? → sinon 🟡 WARNING
5. Tous les paramètres sont immuables (pas de List<T>, pas de mutable class) ? → sinon 🟡 WARNING
6. Nom se termine par "Event" ? → sinon 🟡 WARNING
```

## Format de sortie
```
✅ DomainEvent valid: OrderPlacedEvent
❌ DomainEvent violations: OrderUpdated
   - Not a sealed record → change to: public sealed record OrderUpdatedEvent(...)
   - Missing OccurredAt → add: DateTimeOffset OccurredAt
   - Name should end with 'Event' → rename to OrderUpdatedEvent
```
