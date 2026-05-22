# Rule: No Exceptions for Control Flow

## Principe fondamental
> Les exceptions sont pour l'INATTENDU. Result est pour le PRÉVISIBLE.

## Ce qui est PRÉVISIBLE → Result

| Situation | ❌ Ne pas faire | ✅ Faire |
|-----------|----------------|----------|
| Entity non trouvée | `throw new NotFoundException()` | `return Result.Failure(new NotFoundError(id))` |
| Validation échouée | `throw new ValidationException()` | `return Result.Failure(new ValidationError(...))` |
| Utilisateur non autorisé | `throw new UnauthorizedException()` | `return Result.Failure(new UnauthorizedError())` |
| Règle métier violée | `throw new DomainException()` | `return Result.Failure(new DomainRuleError(...))` |
| Ressource indisponible | `throw new ServiceUnavailableException()` | `return Result.Failure(new UnavailableError(...))` |
| Conflit de données | `throw new ConflictException()` | `return Result.Failure(new ConflictError(...))` |

## Ce qui est IMPRÉVISIBLE → Exception

| Situation | Action |
|-----------|--------|
| Bug de programmation (null inattendu) | `throw new InvalidOperationException()` |
| Corruption mémoire | Laisser propager |
| StackOverflow | Laisser propager |
| OutOfMemoryException | Laisser propager |
| Violation de contrat interne | `throw new ArgumentException()` ou `Debug.Assert` |
| Infrastructure non disponible (DB down) | `Result.Try()` pour capturer + `ErrorCategory.Technical` |

## Frontière infrastructure

Les appels d'infrastructure (DB, HTTP, files) doivent être wrappés:

```csharp
// ✅ Pattern pour les appels d'infra
public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct)
{
    return await Result.TryAsync(async () =>
    {
        var entity = await _dbContext.Users.FindAsync([id], ct);
        return entity is null 
            ? Result.Failure<User>(new UserNotFoundError(id))
            : Result.Success(entity.ToDomain());
    }, ex => new DatabaseError(ex));
}
```

## Exception mapper (couche anti-corruption)

Pour les librairies tierces qui throw:

```csharp
// ✅ Mapper les exceptions d'infra en Result à la frontière
public sealed class HttpClientResultExtensions
{
    public static async ValueTask<Result<T>> GetAsResultAsync<T>(
        this HttpClient client, 
        string url,
        CancellationToken ct = default)
    {
        return await Result.TryAsync(
            async () => await client.GetFromJsonAsync<T>(url, ct) 
                        ?? throw new InvalidOperationException("Null response"),
            ex => ex switch
            {
                HttpRequestException http => new NetworkError(http.StatusCode, http.Message),
                TaskCanceledException => new TimeoutError(url),
                _ => new TechnicalError(ex.Message)
            });
    }
}
```

## Règle des couches

```
Controller/Endpoint    → consomme Result, retourne IActionResult/IResult
Application Service    → retourne Result<T>, ne throw jamais pour flux métier
Domain                 → retourne Result<T>, ne throw jamais pour règles domaine
Infrastructure         → wrapp les exceptions en Result à la frontière
```

## Code review triggers

Ces patterns déclenchent un 🔴 BLOQUANT en review:

```csharp
// ❌ BLOQUANT
catch (SomePredictableException ex)
{
    return Result.Failure(new Error(ex.Message)); // perd le typage fort
}

// ❌ BLOQUANT  
public async Task<User> GetUserAsync(Guid id)
{
    var user = await _repo.FindAsync(id);
    if (user is null) throw new NotFoundException(id); // prévisible!
    return user;
}

// ❌ BLOQUANT
public Result<T> GetValue()
{
    if (!IsSuccess) throw new InvalidOperationException("No value"); // expose throw public
    return _value!;
}
```
