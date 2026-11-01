# Rule: C# Style Guide — MicroKit.Result

## Applicabilité
Toujours actif pour tout fichier `.cs` dans ce projet.

## Namespaces
```csharp
// ✅ File-scoped namespace obligatoire
namespace MicroKit.Result;

// ❌ Block-scoped namespace interdit
namespace MicroKit.Result
{
}
```
Les dossiers ne créent pas de namespace : tous les types du cœur sont dans `MicroKit.Result`, sauf les convertisseurs JSON (`MicroKit.Result.Serialization`) ; le package ASP.NET Core est dans `MicroKit.Result.AspNetCore`.

## Types

### Record structs (value objects immuables)
```csharp
// ✅ Pour les types valeur sans héritage polymorphique
public readonly record struct ErrorCode(string Value)
{
    public static ErrorCode From(string value) => new(value);
    public static implicit operator string(ErrorCode code) => code.Value;
    public override string ToString() => Value;
}
```

### Sealed records (types référence fermés)
```csharp
// ✅ sealed par défaut sur tous les records concrets
public sealed record UserNotFoundError(Guid UserId) 
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound; // sinon Technical (HTTP 500)
}
```

### Classes internes
```csharp
// ✅ internal static pour les helpers d'infrastructure sans état
internal static class ThrowHelper { }
internal static class ResultGuard { }
```

## Constructeurs

### Primary constructors (C# 12+)
```csharp
// ✅ Primary constructor pour DI et value objects
public sealed class DatabaseExceptionMapper(ILogger<DatabaseExceptionMapper> logger)
{
    public IError Map(Exception ex)
    {
        logger.LogError(ex, "Database call failed");
        return new DatabaseError(ex);
    }
}
// Usage : Result<User>.Try(() => repository.Load(id), mapper.Map)
```

### Validation des arguments
```csharp
// ✅ ArgumentException.ThrowIfNullOrWhiteSpace / ArgumentNullException.ThrowIfNull (pas de guard manuel)
public static ErrorCode From(string value)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(value);
    return new(value);
}
```

## Modificateurs

### readonly
```csharp
// ✅ Tout champ non modifié après construction
private readonly IReadOnlyList<IError> _errors;
```

### in parameter modifier
Aucun paramètre de `src/` n'utilise `in` : tous les structs, dont `Result` et `Result<T>` dans chaque extension, sont passés par valeur.
```csharp
// ✅ Par valeur, comme dans toutes les extensions
public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
```

## Méthodes

### Expression-bodied
```csharp
// ✅ Pour one-liners
public bool IsSuccess => _tag == TagSuccess;
public bool IsFailure => _tag == TagFailure; // pas !IsSuccess : default(Result) n'est ni succès ni échec
public override string ToString() => "()"; // Unit
```

### Pattern matching (préféré au casting)
```csharp
// ✅
if (result is { IsSuccess: true, Value: var value }) { }

// ❌
if (result.IsSuccess) { var value = (SomeType)result.Value; }
```

## Null handling
```csharp
// ✅ Null-conditional et null-coalescing
var message = error?.Message ?? "Unknown error";

// ✅ Pattern matching sur null
if (value is null) return Result.Failure(ValidationError.Required(nameof(value)));

// ❌ Jamais de ! (null forgiving) sans commentaire justificatif
var value = result.Value!; // ❌
// ✅ Avec justification:
var value = result.Value!; // Safe: IsSuccess checked above
```

## Documentation XML
```csharp
// ✅ Requis sur tous les membres publics
/// <summary>
/// Maps the value of a successful result using the specified selector.
/// If the result is a failure, the error is propagated unchanged.
/// </summary>
/// <typeparam name="TIn">The source value type.</typeparam>
/// <typeparam name="TOut">The target value type.</typeparam>
/// <param name="result">The source result.</param>
/// <param name="mapper">The transformation function.</param>
/// <returns>A new result with the mapped value, or the original failure.</returns>
/// <example>
/// <code>
/// Result&lt;string&gt; name = GetUser(id).Map(u => u.Name);
/// </code>
/// </example>
public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
```

## Ordering des membres dans une classe/record
1. Constants
2. Static fields
3. Instance fields (readonly)
4. Constructors / primary ctor params
5. Static properties
6. Instance properties  
7. Static methods (factory)
8. Public methods
9. Protected methods
10. Private methods
11. Operator overloads
12. Implicit/explicit conversions
