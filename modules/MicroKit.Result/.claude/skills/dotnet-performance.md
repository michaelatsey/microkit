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
| `ResultProblemDetailsFactory.CreateProblemDetails(error)` | < 1µs | ~200 bytes (acceptable) |

## Techniques d'optimisation appliquées

### 1. readonly record struct pour Unit
```csharp
// 0 allocation, 0 bytes de données ; toutes les valeurs sont égales (record struct sans champ)
public readonly record struct Unit
{
    public static readonly Unit Value;
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
    internal static void ThrowArgumentNull(string paramName) =>
        throw new ArgumentNullException(paramName);
    
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowResultUninitialized() =>
        throw new ResultException(
            "Result was not initialized. Use Result.Success() or Result.Failure() factory methods.");
}

// Usage dans le hot path (ResultGuard.NotNull délègue le throw à ThrowHelper.ThrowArgumentNull) :
public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
{
    ResultGuard.NotNull(mapper);                     // branche froide
    return result.IsSuccess                          // branche chaude
        ? Result<TOut>.Success(mapper(result.Value))
        : Result<TOut>.Failure(result.Error);
}
```

### 3. AggressiveInlining pour les one-liners chauds
```csharp
public bool IsSuccess
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _tag == TagSuccess;
}

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
//  ✅ Wrapper struct autour d'un array — aucune allocation en plus du tableau
//  ✅ Énumérateur struct : pas d'allocation pour l'itération
//  ⚠️ Égalité = référence du tableau sous-jacent, pas égalité par valeur
//  ❌ Modification = réallocation totale (ok car immuable)

public sealed class ErrorCollection : IError, IReadOnlyList<IError>
{
    private readonly ImmutableArray<IError> _errors;

    // Pas de surcharge à une seule erreur : un tableau params, ou une séquence
    public static ErrorCollection From(params IError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
            throw new ArgumentException("An ErrorCollection must contain at least one error.", nameof(errors));
        return new ErrorCollection(ImmutableArray.Create(errors));
    }

    public static ErrorCollection From(IEnumerable<IError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var array = errors.ToImmutableArray();
        if (array.IsEmpty)
            throw new ArgumentException("An ErrorCollection must contain at least one error.", nameof(errors));
        return new ErrorCollection(array);
    }
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
// ❌ Capture de this → un délégué alloué à chaque appel
public Result<UserDto> ToDto(Result<User> result)
    => result.Map(u => _mapper.Map(u));

// ❌ Capture d'un paramètre → une closure + un délégué à chaque appel
public static Result<UserDto> ToDto(Result<User> result, IMapper mapper)
    => result.Map(u => mapper.Map(u));

// ❌ Method group d'instance → un délégué à chaque appel aussi
public static Result<UserDto> ToDtoGroup(Result<User> result, IMapper mapper)
    => result.Map(mapper.Map);

// ✅ Lambda statique (aucune capture) → délégué mis en cache par le compilateur, 0 allocation
public static Result<UserDto> ToDtoStatic(Result<User> result)
    => result.Map(static u => new UserDto(u.Id, u.Name));
```

## BenchmarkDotNet baseline

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
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
