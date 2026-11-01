---
name: dotnet-performance
description: Use when making or reviewing an allocation or latency claim about MicroKit.Result hot paths (Map, Bind, Match, ErrorCollection), or when writing a BenchmarkDotNet benchmark for them. Not for general code review or for choosing between Map and Bind.
---

# Skill: .NET Performance pour MicroKit.Result

## La règle : toute affirmation est mesurée

Aucun benchmark n'existe aujourd'hui : `tests/MicroKit.Result.PerformanceTests` ne contient que son `.csproj`. #184 Phase 7 publie les mesures dans le README, avec la machine et le SDK, contre ErrorOr et FluentResults : chemin succès, chemin échec, chaîne `Map`/`Bind`, allocation par appel.

D'ici là, ce skill ne donne aucun chiffre. Une affirmation de performance est soit une sortie de BenchmarkDotNet avec sa machine et son SDK, soit une hypothèse présentée comme telle.

## Hypothèses à mesurer

| Opération | Hypothèse |
|-----------|-----------|
| `Result.Success(value)` | Aucune allocation : `Result<T>` est un struct |
| `Result.Failure(error)` | Le coût de créer l'erreur, rien de plus |
| `.Map(lambda)`, `.Bind(lambda)` | Aucune allocation avec une lambda sans capture |
| `.Match(f, g)` | Aucune allocation avec des lambdas sans capture |
| `ResultProblemDetailsFactory.CreateProblemDetails(error)` | Quelques allocations par appel, sur le rail d'échec seulement |
| `ValueTask<T>` face à `Task<T>` | Une allocation évitée quand le résultat est disponible de façon synchrone |

## Techniques employées

### 1. readonly record struct pour Unit
```csharp
// Toutes les valeurs sont égales (record struct sans champ)
public readonly record struct Unit
{
    public static readonly Unit Value;
    public override string ToString() => "()";
}
```

### 2. ThrowHelper pour les guards
```csharp
// Le throw vit hors de la méthode appelante. [DoesNotReturn] sert l'analyse null-state du compilateur
// (Microsoft Learn, "Attributes for null-state static analysis"), pas le JIT. Gain non mesuré.
internal static class ThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentNull(string paramName) =>
        throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowResultUninitialized() =>
        throw new ResultException(
            "Result was not initialized. Use Result.Success() or Result.Failure() factory methods.");
}

// Usage (ResultGuard.NotNull délègue le throw à ThrowHelper.ThrowArgumentNull) :
public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
{
    ResultGuard.NotNull(mapper);
    return result.IsSuccess
        ? Result<TOut>.Success(mapper(result.Value))
        : Result<TOut>.Failure(result.Error);
}
```

### 3. Attributs JIT
`src/` emploie `AggressiveInlining` sur ses accesseurs et guards ; son effet n'est pas mesuré. Microsoft Learn (*MethodImplOptions*) :

- `AggressiveInlining` : un usage inutile peut dégrader les performances. « Always measure performance to ensure it's helpful to apply this attribute. »
- `AggressiveOptimization` : « It's rarely appropriate to use this attribute. » La méthode saute le premier niveau du tiered compilation et perd notamment le dynamic PGO. `src/` ne l'emploie pas.

Pas de nouvel attribut sans benchmark avant et après.

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
//  ✅ Wrapper struct autour d'un array
//  ✅ Énumérateur struct
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

### 6. ValueTask<T>
`ValueTask<T>` est la convention racine ; son gain face à `Task<T>` n'est pas mesuré, la Phase 7 le mesure. Il s'attend directement et une seule fois, sans `.Result` ni `.GetAwaiter().GetResult()` avant complétion (CA2012).

```csharp
public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)
{
    if (_cache.TryGetValue(id, out var cached))
        return Result.Success(cached); // retour synchrone

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

// ✅ Lambda statique (aucune capture) → délégué mis en cache par le compilateur
public static Result<UserDto> ToDtoStatic(Result<User> result)
    => result.Map(static u => new UserDto(u.Id, u.Name));
```

## Écrire un benchmark

Point de départ pour mesurer une hypothèse. Les benchmarks publiés et leur périmètre relèvent de la Phase 7.

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class ResultCoreBenchmarks
{
    // Champs d'instance : CA1822 refuse sinon ces méthodes en Release, et BenchmarkDotNet les veut non statiques
    private readonly int _value = 42;
    private readonly Result<int> _successResult = Result.Success(42);
    private readonly Result<int> _failureResult = Result.Failure<int>(new BenchmarkError());

    [Benchmark(Baseline = true)]
    public int DirectCall() => _value * 2; // baseline: pure computation

    [Benchmark]
    public Result<int> Map_Success() => _successResult.Map(x => x * 2);

    [Benchmark]
    public Result<int> Map_Failure() => _failureResult.Map(x => x * 2);

    [Benchmark]
    public int Match_Success() => _successResult.Match(v => v, _ => -1);
}
```
