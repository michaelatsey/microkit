# Rule: C# Style Guide — MicroKit.Result

## Applicabilité
Toujours actif pour tout fichier `.cs` dans ce projet.

## Namespaces
```csharp
// ✅ File-scoped namespace obligatoire
namespace MicroKit.Result.Core;

// ❌ Block-scoped namespace interdit
namespace MicroKit.Result.Core
{
}
```

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
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found");
```

### Classes internes
```csharp
// ✅ sealed + internal pour les helpers d'infrastructure
internal static class ThrowHelper { }
internal sealed class ResultGuard { }
```

## Constructeurs

### Primary constructors (C# 12+)
```csharp
// ✅ Primary constructor pour DI et value objects
public sealed class DefaultExceptionMapper(ILogger<DefaultExceptionMapper> logger) 
    : IExceptionMapper
{
    public Result Map(Exception ex) => logger.LogAndMap(ex);
}
```

### Validation dans les constructeurs
```csharp
// ✅ ArgumentNullException.ThrowIfNull (pas de guard manuel)
public Error(ErrorCode code, string message)
{
    ArgumentNullException.ThrowIfNull(message);
    ArgumentException.ThrowIfNullOrWhiteSpace(message);
    Code = code;
    Message = message;
}
```

## Modificateurs

### readonly
```csharp
// ✅ Tout champ non modifié après construction
private readonly IReadOnlyList<IError> _errors;
```

### in parameter modifier
```csharp
// ✅ Pour les gros structs passés sans modification
public static Result<T> Ensure<T>(this in Result<T> result, ...)
// Note: utiliser avec parcimonie, seulement pour structs > 16 bytes
```

## Méthodes

### Expression-bodied
```csharp
// ✅ Pour one-liners
public bool IsSuccess => _state == ResultState.Success;
public bool IsFailure => !IsSuccess;
public override string ToString() => IsSuccess ? $"Success({Value})" : $"Failure({Error})";
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
if (value is null) return Result.Failure(new NullValueError());

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
