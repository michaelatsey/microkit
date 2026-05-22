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
    ...
}

// Avec Bind:
var final = await GetUser(id)
    .BindAsync(u => GetCart(u.CartId))
    .BindAsync(c => ValidateCart(c))
    .BindAsync(c => CreateOrder(c));
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
var result = Result.CombineAll(
    ValidateName(command.Name),
    ValidateEmail(command.Email),
    ValidateAge(command.Age)
);
// Si toutes success → Result.Success(Unit)
// Si certaines fail → Result.Failure(ErrorCollection{errors...})
```

## Patterns avancés

### Dead End (TapError)
```csharp
// Logguer l'erreur sans la transformer
result.TapError(e => _logger.LogError("Operation failed: {Code}", e.Code));
```

### Doubling the track (Map + pattern)
```csharp
// Transformer les deux rails simultanément
result.Match(
    onSuccess: v => Result.Success(v.ToDto()),
    onFailure: e => Result.Failure<UserDto>(e.Enrich(correlationId))
);
```

### Async railway
```csharp
// Pipeline entièrement async
Result<InvoiceDto> invoice = await GetUserAsync(userId)
    .BindAsync(u => GetActiveCartAsync(u.Id))
    .EnsureAsync(c => !c.IsEmpty, new EmptyCartError())
    .BindAsync(c => ApplyDiscountsAsync(c, promoCode))
    .BindAsync(c => CreateInvoiceAsync(c))
    .MapAsync(i => i.ToDto())
    .TapAsync(i => PublishInvoiceCreatedEventAsync(i));
```

## Pièges courants

### Le Bind nested inutile
```csharp
// ❌ Nested — signe d'un Bind manqué
Result<Result<User>> nested = outer.Map(x => GetUser(x.Id));
// ✅ Flat
Result<User> flat = outer.Bind(x => GetUser(x.Id));
```

### Le Tap qui modifie
```csharp
// ❌ Tap ne doit pas transformer
result.Tap(user => { user.Name = "modified"; return user; }); // mutation!
// ✅ Tap = side-effect pur
result.Tap(user => _logger.Log(user.Id));
```

### L'Ensure avec erreur non typée
```csharp
// ❌ Perd le typage fort
result.Ensure(u => u.IsActive, new Error("USER_INACTIVE", "..."));
// ✅ Erreur typée
result.Ensure(u => u.IsActive, new UserInactiveError(u.Id));
```

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
