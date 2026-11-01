# MicroKit.Result — Project Brain

## 🎯 Mission
Librairie .NET 10+ pour la gestion explicite des résultats, erreurs et workflows fonctionnels.
Remplace le pattern exception-as-flow par des types expressifs, composables et performants.

## 🏛️ Philosophie Architecturale

### Railway-Oriented Programming (ROP)
Tous les flux passent sur deux rails : **Success** | **Failure**.
Jamais d'exception pour un flux attendu. Les exceptions restent pour l'inattendu.

### Hiérarchie des types
```
Result          ← sans valeur (commandes, side-effects)
Result<T>       ← avec valeur (queries, transformations)
Unit            ← void-safe pour les génériques
```

### Règle d'or
> **Si c'est prévisible → Result. Si c'est imprévisible → Exception.**

## 📐 Conventions de Code

### Naming
- Types: `PascalCase`, `sealed` par défaut
- Méthodes d'extension: verbes fonctionnels (`Map`, `Bind`, `Match`, `Tap`, `Ensure`)
- Erreurs: noun phrases (`UserNotFoundError`, `PaymentDeclinedError`) ; la librairie ne fournit que `ValidationError` et `ErrorCollection`, les erreurs de domaine dérivent de `Error`
- Codes d'erreur: `DOMAIN.ENTITY.ACTION` ex: `AUTH.USER.NOT_FOUND`

### Style C# 2026
- Primary constructors partout où pertinent
- File-scoped namespaces obligatoires
- Nullable Reference Types: `enable`
- `readonly record struct` pour les value objects sans héritage
- `sealed record` pour les types référence fermés
- `ValueTask<T>` pour async léger (pas d'await multiple)
- `Task<T>` pour async avec await multiple ou continuation complexe

### Interdictions
- ❌ `throw` dans les flux métier normaux
- ❌ `null` retourné depuis une méthode publique (utiliser `Result` ou `Result<T>`)  
- ❌ `Exception` pour représenter une validation ou une règle métier
- ❌ Réflexion runtime sauf dans la couche Serialization (NativeAOT)
- ❌ `static` mutable partagé (thread-safety)

## 🧱 Structure des Erreurs

```csharp
// Toujours créer des erreurs typées pour le domaine
public sealed record UserNotFoundError(Guid UserId) 
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}
```

## 🔄 Patterns Autorisés

### Map (transformation valeur)
```csharp
Result<User> userResult = GetUser(id);
Result<UserDto> dto = userResult.Map(u => mapper.ToDto(u));
```

### Bind (chaînage de résultats)
```csharp
Result<Order> order = GetUser(id)
    .Bind(user => GetCart(user.CartId))
    .Bind(cart => CreateOrder(cart));
```

### Match (consommation finale)
```csharp
// Équivalent de result.ToHttpResult() (MicroKit.Result.AspNetCore)
IResult response = result.Match(
    onSuccess: value => Results.Ok(value),
    onFailure: error => Results.Problem(ResultProblemDetailsFactory.CreateProblemDetails(error))
);
```

### Tap (side-effect sans transformation)
```csharp
// UserLoaded : méthode [LoggerMessage], déclarée dans rules/result-patterns.md
Result<User> logged = userResult.Tap(u => logger.UserLoaded(u.Id));
```

### Ensure (validation inline)
```csharp
Result<User> validated = userResult
    .Ensure(u => u.IsActive, new UserInactiveError(userId));
```

## 🚀 Performance Guidelines

1. Préférer `Result` (non-generic) pour les opérations sans valeur de retour
2. Éviter `ToList()` dans les pipelines — utiliser `IEnumerable<T>` lazy
3. `ValueTask` pour les méthodes async appelées souvent avec résultat synchrone
4. Pas d'allocation dans `Map`/`Bind` sur le chemin succès : `Result<T>` est un struct ; une lambda sans capture est mise en cache, une lambda qui capture alloue à chaque appel
5. `ErrorCollection` est matérialisée à la construction (`ImmutableArray<IError>`, `Message` calculé une fois) — ne la créer que sur le rail d'échec

## 🧪 Testing Conventions

```csharp
// Arrange
var sut = new UserService(mockRepo);

// Act  
Result<User> result = await sut.GetUserAsync(userId);

// Assert - toujours tester les deux rails, chacun dans son test
result.IsSuccess.ShouldBeTrue();
result.Value.ShouldBe(expected);

// ... et pour le rail échec :
result.IsFailure.ShouldBeTrue();
result.Error.ShouldBeOfType<UserNotFoundError>();
```

## 📦 Packages
- `MicroKit.Result` — core (zéro dépendance) ; inclut les convertisseurs JSON (namespace `MicroKit.Result.Serialization`)
- `MicroKit.Result.AspNetCore` — extensions HTTP/ProblemDetails

## 🔗 Références
- [Railway Oriented Programming - Scott Wlaschin](https://fsharpforfunandprofit.com/rop/)
- [Error handling in .NET — Microsoft](https://learn.microsoft.com/dotnet/standard/exceptions/)
- [OneOf library](https://github.com/mcintyre321/OneOf) — inspiration pattern
- [FluentResults](https://github.com/altmann/FluentResults) — inspiration API
