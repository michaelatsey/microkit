# Skill: .NET Performance pour MicroKit.Result

## Quand activer ce skill
- Hot paths (Map, Bind, Match appelés millions de fois)
- Benchmarking et comparaison d'implémentations
- Review d'allocations GC
- Optimisation pour NativeAOT

## Métriques cibles pour MicroKit.Result

| Opération | Objectif latence | Objectif allocations |
|-----------|-----------------|---------------------|
| `Result.Success(value)` | < 1ns | 0 bytes (struct) |
| `Result.Failure(error)` | < 5ns | sizeof(Error) record |
| `.Map(lambda)` | < 5ns | 0 bytes si inliné |
| `.Bind(lambda)` | < 10ns | 0 bytes si inliné |
| `.Match(f, g)` | < 5ns | 0 bytes |
| `.ToProblemDetails()` | < 1µs | ~200 bytes (acceptable) |

## Techniques d'optimisation appliquées

### 1. readonly record struct pour Unit
```csharp
// 0 allocation, 0 bytes de données, reference-equal par défaut
public readonly record struct Unit
{
    public static readonly Unit Value = default;
    public override string ToString() => "()";
}
```

### 2. ThrowHelper pour les guards
```csharp
// JIT peut inliner la vérification et branch-predict le happy path
internal static class ThrowHelper
{
    // [DoesNotReturn] hint permet au JIT d'optimiser la branche normale
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)] // Ne pas inliner le lancement
    public static void ThrowArgumentNull(string paramName)
        => throw new ArgumentNullException(paramName);
    
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperation(string message)
        => throw new InvalidOperationException(message);
}

// Usage dans le hot path:
public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
{
    if (mapper is null) ThrowHelper.ThrowArgumentNull(nameof(mapper)); // branche froide
    return result.IsSuccess                                             // branche chaude
        ? Result.Success(mapper(result.Value))
        : Result.Failure<TOut>(result.Error);
}
```

### 3. AggressiveInlining pour les one-liners chauds
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsSuccess(this Result result) => result._state == ResultState.Success;

// Note: seulement pour les méthodes < 32 bytes IL environ
// Ne pas abuser — le JIT inline déjà les petites méthodes
```

### 4. Éviter le boxing des value types
```csharp
// ❌ Boxing: ErrorCode (struct) → object (dans Dictionary)
var metadata = new Dictionary<string, object?> { ["code"] = errorCode };

// ✅ Pas de boxing: utiliser le type string directement
var metadata = new Dictionary<string, object?> { ["code"] = errorCode.Value };

// ❌ Boxing via interface
IError error = new MyError(); // si MyError est un struct
// ✅ Utiliser sealed record (reference type) pour les erreurs
```

### 5. ImmutableArray vs List pour ErrorCollection
```csharp
// ImmutableArray<T>: 
//  ✅ Stack-allocated wrapper autour d'un array
//  ✅ Pas d'allocation supplémentaire pour l'itération
//  ✅ Comparable par valeur (important pour les tests)
//  ❌ Modification = réallocation totale (ok car immuable)

public sealed class ErrorCollection
{
    private readonly ImmutableArray<IError> _errors;
    
    // Single error path — très fréquent
    public static ErrorCollection From(IError error) 
        => new(ImmutableArray.Create(error));
    
    // Multiple errors path
    public static ErrorCollection From(params IError[] errors)
        => new(errors.ToImmutableArray());
}
```

### 6. ValueTask pour les méthodes async souvent synchrones
```csharp
// ValueTask évite l'allocation de Task si le résultat est déjà disponible (cache hit, etc.)
public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct)
{
    // Si le user est en cache → retour synchrone, 0 allocation Task
    if (_cache.TryGetValue(id, out var cached))
        return Result.Success(cached); // ValueTask.FromResult interne
    
    // Si pas en cache → await réel, mais alors l'allocation Task est justifiée
    var user = await _repo.FindAsync(id, ct).ConfigureAwait(false);
    return user is null 
        ? Result.Failure<User>(new UserNotFoundError(id))
        : Result.Success(user);
}
```

### 7. Éviter les closures qui capturent `this`
```csharp
// ❌ Capture implicite de this → allocation de closure
public Result<UserDto> ToDto(Result<User> result) 
    => result.Map(u => _mapper.Map(u)); // _mapper capture this

// ✅ Passer comme paramètre → pas de closure si méthode statique
public static Result<UserDto> ToDto(Result<User> result, IMapper mapper)
    => result.Map(u => mapper.Map(u)); // si mapper est local, pas de capture this
// Ou encore mieux pour le hot path:
public static Result<UserDto> ToDto(Result<User> result, IMapper mapper)
    => result.Map(mapper.Map); // method group — 0 allocation de lambda
```

## BenchmarkDotNet baseline

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class ResultCoreBenchmarks
{
    private static readonly Result<int> _successResult = Result.Success(42);
    private static readonly Result<int> _failureResult = Result.Failure<int>(new BenchmarkError());
    
    [Benchmark(Baseline = true)]
    public int DirectCall() => 42 * 2; // baseline: pure computation
    
    [Benchmark]
    public Result<int> Map_Success() => _successResult.Map(x => x * 2);
    
    [Benchmark]
    public Result<int> Map_Failure() => _failureResult.Map(x => x * 2);
    
    [Benchmark]
    public int Match_Success() => _successResult.Match(v => v, _ => -1);
}
```
