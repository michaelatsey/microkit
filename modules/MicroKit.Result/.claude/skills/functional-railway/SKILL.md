---
name: functional-railway
description: Use when composing MicroKit.Result pipelines with Map, Bind, Tap, Ensure, Match, Combine or their async variants, or debugging a pipeline that does not compose. Not for designing error types or for performance questions.
---

# Skill: Functional Railway-Oriented Programming

## Quand activer ce skill
- Quand on compose un pipeline avec `Map`, `Bind`, `Tap`, `Ensure`, `Match`, `Combine` ou leurs variantes async
- Quand on débug un pipeline qui ne compose pas

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
    .BindAsync(async u => await GetCartAsync(u.CartId, ct))
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
| Transformer l'erreur | MapError | `Error → Error` |
| Repli sur échec | Compensate | `Error → Result<A>` |
| Consommation finale | Match | `A → C, Error → C` |

### Parallel composition (Combine)
```csharp
// Toutes les opérations indépendantes exécutées, erreurs collectées
var result = Result.CombineAll(
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

### Doubling the track (Map + MapError)
```csharp
// Transformer les deux rails : Map sur le succès, MapError sur l'échec
// (EnrichedError : erreur applicative qui enveloppe l'erreur d'origine, et en reprend
// Category, Severity et Metadata ; sinon elle répond 500, samples/.../Readme/Pipelines.cs)
Result<UserDto> mapped = result
    .Map(v => v.ToDto())
    .MapError(e => new EnrichedError(e, correlationId));
```

### Async railway
```csharp
// Les ports retournent ValueTask<Result<T>> et prennent ct (convention racine) : ils passent par la forme
// à jeton des extensions, dont le délégué reçoit le CancellationToken et retourne un ValueTask
// (ADR-RESULT-001 D8). Le ct est passé explicitement : omis, le délégué reçoit CancellationToken.None.
Result<User> user = await GetUserAsync(userId, ct);
Result<Cart> cart = await user.BindAsync((u, token) => GetActiveCartAsync(u.Id, token), ct);
Result<Cart> discounted = await cart
    .Ensure(c => !c.IsEmpty, new EmptyCartError())   // erreur construite à chaque appel
    .BindAsync((c, token) => ApplyDiscountsAsync(c, promoCode, token), ct);
Result<InvoiceDto> invoice = await discounted
    .BindAsync(async c => await CreateInvoiceAsync(c, ct))
    .Map(i => i.ToDto());

// Un effet qui retourne un Result passe par BindAsync : TapAsync prend un Func<T, Task>,
// et y jetterait l'échec de la publication sans diagnostic
invoice = await invoice.BindAsync(async i =>
    (await PublishInvoiceCreatedEventAsync(i, ct)).Match(
        onSuccess: () => Result.Success(i),
        onFailure: e => Result.Failure<InvoiceDto>(e)));
```

Les extensions sur `Task<Result<T>>` et `ValueTask<Result<T>>` prennent des délégués synchrones, sauf `Tap`, `TapError` et `Finally`. Un lambda async y compile quand même :

- dans `Map` ou `Match`, il retourne un `Task` imbriqué dans le résultat ;
- dans `Tap`, `TapError` ou `Finally`, sur `Result<T>`, `Result` ou une tâche, il se lie à la surcharge qui attend l'effet et retourne un `ValueTask` : l'appel s'attend.

Un délégué async de `Map`, `Bind`, `Ensure`… passe donc par `MapAsync`, `BindAsync`, `EnsureAsync`… sur un `Result<T>` déjà attendu.

Ces opérations existent sur `Result<T>`. Le `Result` non générique les a aussi, sync et async (ADR-RESULT-001 D9), et `Result<T>.Bind(Func<T, Result>)` mène d'une valeur à une commande.

## Pièges courants

### Le Bind nested inutile
```csharp
// ❌ Nested — signe d'un Bind manqué
Result<Result<User>> nested = outer.Map(x => GetUser(x.Id));
// ✅ Flat
Result<User> flat = outer.Bind(x => GetUser(x.Id));
```

### Le Tap qui transforme
```csharp
// ❌ Tap modifie la valeur : le rail de succès porte un objet changé en douce
Result<User> mutated = result.Tap(u => u.Rename(newName));
// ✅ Map retourne la nouvelle valeur
Result<User> renamed = result.Map(u => u.WithName(newName));
```

Les autres usages attendus de `Tap` et `Ensure` sont dans `rules/result-patterns.md`.

## Composition avec LINQ Query Syntax

```csharp
// Pour les fans du LINQ, Result supporte la syntaxe de requête
var result = 
    from user in GetUser(id)
    from cart in GetCart(user.CartId)
    from order in CreateOrder(cart)
    select order.ToDto();
// Équivaut à GetUser(id).Bind(u => GetCart(u.CartId)).Bind(c => CreateOrder(c)).Map(o => o.ToDto()) :
// synchrone, et sans l'étape ValidateCart de la chaîne plus haut
```
