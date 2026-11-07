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

### 1. ThrowHelper pour les guards
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

### 2. Attributs JIT
`src/` emploie `AggressiveInlining` sur ses accesseurs et guards ; son effet n'est pas mesuré. Microsoft Learn (*MethodImplOptions*) :

- `AggressiveInlining` : un usage inutile peut dégrader les performances. « Always measure performance to ensure it's helpful to apply this attribute. »
- `AggressiveOptimization` : « It's rarely appropriate to use this attribute. » La méthode saute le premier niveau du tiered compilation et perd notamment le dynamic PGO. `src/` ne l'emploie pas.

Pas de nouvel attribut sans benchmark avant et après.

### 3. Éviter le boxing dans les erreurs et leurs métadonnées
```csharp
// ❌ Boxing: ErrorCode (struct) → object (dans Dictionary)
var metadata = new Dictionary<string, object?> { ["code"] = errorCode };

// ✅ Pas de boxing: utiliser le type string directement
var metadata = new Dictionary<string, object?> { ["code"] = errorCode.Value };
// …mais le type stocké change ce qui se relit : TryGetMetadata<ErrorCode>("code") retourne
// alors default, il faut TryGetMetadata<string>("code"). Le lecteur et l'écrivain s'accordent.
```

Une erreur est un `sealed record`, donc un type référence : elle est allouée une fois, et la voir comme `IError` ne la boxe pas. Un `struct` qui implémenterait `IError` serait boxé à chaque passage en `IError`, ce que fait tout `Result.Failure` : rien à y gagner.

### 4. ImmutableArray vs List pour ErrorCollection
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

### 5. ValueTask<T>
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

### 6. Closures et délégués : des hypothèses à mesurer
Une lambda qui capture crée un délégué, et souvent une closure, à chaque appel. Que cela alloue sur le tas dépend du contexte : .NET 10 peut allouer sur la pile un délégué qui ne s'échappe pas, selon ce que le JIT inline. Chaque forme ci-dessous est donc une hypothèse, qu'un benchmark confirme dans le contexte réel d'appel.

```csharp
// ⚠️ Capture de this : un délégué par appel, peut-être alloué sur le tas
public Result<UserDto> ToDto(Result<User> result)
    => result.Map(u => _mapper.Map(u));

// ⚠️ Capture d'un paramètre : une closure et un délégué par appel, peut-être sur le tas
public static Result<UserDto> ToDto(Result<User> result, IMapper mapper)
    => result.Map(u => mapper.Map(u));

// ⚠️ Method group d'instance : un délégué par appel, peut-être sur le tas
public static Result<UserDto> ToDtoGroup(Result<User> result, IMapper mapper)
    => result.Map(mapper.Map);

// ✅ Lambda statique (aucune capture) : délégué mis en cache par le compilateur.
// Équivalent aux formes ci-dessus seulement si le mapping n'a besoin d'aucun état
public static Result<UserDto> ToDtoStatic(Result<User> result)
    => result.Map(static u => UserMapper.ToDto(u));
```

## Écrire un benchmark

Point de départ pour mesurer une hypothèse. Les benchmarks publiés et leur périmètre relèvent de la Phase 7.

`tests/MicroKit.Result.PerformanceTests` ne produit aujourd'hui aucune mesure : le Test SDK y génère un `Main` vide, si bien que `dotnet run` sort 0 sans rien afficher et que `dotnet test` ne trouve aucun test. Pour mesurer, le projet doit porter `<GenerateProgramFile>false</GenerateProgramFile>` (sinon un `Main` à soi échoue en CS7022) et un `Program.cs` qui lance BenchmarkDotNet. Ce changement du `.csproj` appartient à la Phase 7. Une fois fait, depuis `modules/MicroKit.Result/` :

```csharp
// Program.cs
BenchmarkSwitcher.FromAssembly(typeof(ResultCoreBenchmarks).Assembly).Run(args);
```

```bash
dotnet run -c Release --project tests/MicroKit.Result.PerformanceTests -- --filter '*'
```

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class ResultCoreBenchmarks
{
    // Champs d'instance : CA1822 refuse sinon ces méthodes en Release, et BenchmarkDotNet les veut non statiques
    private readonly int _value = 42;
    private readonly Result<int> _successResult = Result.Success(42);
    private readonly Result<int> _failureResult = Result.Failure<int>(new BenchmarkError());

    // Pas de Baseline : sur un calcul proche de zéro, chaque ratio s'afficherait « ? »
    [Benchmark]
    public int DirectCall() => _value * 2;

    [Benchmark]
    public Result<int> Map_Success() => _successResult.Map(x => x * 2);

    [Benchmark]
    public Result<int> Map_Failure() => _failureResult.Map(x => x * 2);

    [Benchmark]
    public int Match_Success() => _successResult.Match(v => v, _ => -1);
}
```
