---
name: functional-railway
description: Use when composing MicroKit.Result pipelines with Map, Bind, Tap, Ensure, Match, Combine or their async variants, or debugging a pipeline that does not compose. Not for designing error types or for performance questions.
---

# Skill: Functional Railway-Oriented Programming

## Quand activer ce skill
- Quand on conçoit un nouveau pipeline de transformation
- Quand on chaîne des opérations Result
- Quand on adapte des patterns F# au C#
- Quand on débug un pipeline qui ne compose pas bien

## Concepts fondamentaux

### Les deux rails
```
Input ──→ [Op1] ──→ [Op2] ──→ [Op3] ──→ Success Output
              ↘failure      ↘failure      ↘
               [Error] ──────────────────→ Failure Output
```

Une fois sur le rail d'erreur, toutes les opérations suivantes sont by-passées.

### Switch functions (Bind)
Transforme une fonction `A → Result<B>` en `Result<A> → Result<B>`:
```csharp
// Sans Bind: 
if (result.IsSuccess)
{
    var inner = result.Value;
    var next = await NextOperation(inner);
    if (!next.IsSuccess) return Result.Failure<C>(next.Error);
    return await LastOperation(next.Value);
}
return Result.Failure<C>(result.Error);

// Avec Bind : un délégué async part d'un Result<T> (BindAsync) ;
// sur un Task/ValueTask<Result<T>>, Bind n'accepte qu'un délégué synchrone
var final = await GetUser(id)
    .BindAsync(u => GetCartAsync(u.CartId))
    .Bind(c => ValidateCart(c))
    .Bind(c => CreateOrder(c));
```

### Map vs Bind — quand utiliser quoi

| Situation | Opération | Signature |
|-----------|-----------|-----------|
| Transformer une valeur (jamais fail) | Map | `A → B` |
| Opération qui peut échouer | Bind | `A → Result<B>` |
| Side-effect (log, event) | Tap | `A → void` |
| Validation | Ensure | `A → bool` + error |
| Consommation finale | Match | `A → C, Error → C` |

### Parallel composition (Combine)
```csharp
// Toutes les opérations indépendantes exécutées, erreurs collectées
var result = ResultCombineExtensions.CombineAll(
    ValidateName(command.Name),
    ValidateEmail(command.Email),
    ValidateAge(command.Age)
);
// Si toutes success → Result.Success()
// Si certaines fail → Result.Failure(ErrorCollection)
```

## Patterns avancés

### Dead End (TapError)
```csharp
// Logguer l'erreur sans la transformer (OperationFailed : [LoggerMessage], rules/result-patterns.md)
Result<User> observed = result.TapError(e => logger.OperationFailed(e.Code));
```

### Doubling the track (Map + pattern)
```csharp
// Transformer les deux rails simultanément
// (EnrichedError : erreur applicative qui enveloppe l'erreur d'origine)
Result<UserDto> mapped = result.Match(
    onSuccess: v => Result.Success(v.ToDto()),
    onFailure: e => Result.Failure<UserDto>(new EnrichedError(e, correlationId))
);
```

### Async railway
```csharp
// Pipeline async — les extensions sur Task/ValueTask<Result<T>> n'acceptent que des délégués
// synchrones ; un délégué async (BindAsync, MapAsync, EnsureAsync, TapAsync…) part d'un
// Result<T> déjà attendu, et doit retourner un Task (pas un ValueTask).
Result<User> user = await GetUserAsync(userId);
Result<Cart> cart = await user.BindAsync(u => GetActiveCartAsync(u.Id));
Result<Cart> discounted = await cart
    .Ensure(c => !c.IsEmpty, new EmptyCartError())
    .BindAsync(c => ApplyDiscountsAsync(c, promoCode));
Result<InvoiceDto> invoice = await discounted
    .BindAsync(c => CreateInvoiceAsync(c))
    .Map(i => i.ToDto());
invoice = await invoice.TapAsync(i => PublishInvoiceCreatedEventAsync(i));
```

Ces opérations existent sur `Result<T>`. Le `Result` non générique n'a aujourd'hui que `Match`, `Tap`, `TapError` et `Finally`, et aucun async hors `TryAsync` ; #184 Phase 4 les ajoute.

## Pièges courants

### Le Bind nested inutile
```csharp
// ❌ Nested — signe d'un Bind manqué
Result<Result<User>> nested = outer.Map(x => GetUser(x.Id));
// ✅ Flat
Result<User> flat = outer.Bind(x => GetUser(x.Id));
```

Les usages attendus de `Tap` et `Ensure` sont dans `rules/result-patterns.md`.

## Composition avec LINQ Query Syntax

```csharp
// Pour les fans du LINQ, Result supporte la syntaxe de requête
var result = 
    from user in GetUser(id)
    from cart in GetCart(user.CartId)
    from order in CreateOrder(cart)
    select order.ToDto();
// Équivalent au Bind chain ci-dessus
```
