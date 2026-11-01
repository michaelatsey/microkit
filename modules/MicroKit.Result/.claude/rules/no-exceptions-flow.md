# Rule: No Exceptions for Control Flow

## Principe fondamental
> Les exceptions sont pour l'INATTENDU. Result est pour le PRÉVISIBLE.

## Ce qui est PRÉVISIBLE → Result

| Situation | ❌ Ne pas faire | ✅ Faire |
|-----------|----------------|----------|
| Entity non trouvée | `throw new NotFoundException()` | `return Result.Failure(new UserNotFoundError(id))` |
| Validation échouée | `throw new ValidationException()` | `return Result.Failure(ValidationError.Required("Email"))` |
| Utilisateur non autorisé | `throw new UnauthorizedException()` | `return Result.Failure(new UnauthorizedOperationError(operation, userId))` |
| Règle métier violée | `throw new DomainException()` | `return Result.Failure(new OrderAlreadyShippedError(orderId, shippedAt))` |
| Ressource indisponible | `throw new ServiceUnavailableException()` | `return Result.Failure(new PaymentGatewayUnavailableError(gateway))` |
| Conflit de données | `throw new ConflictException()` | `return Result.Failure(new EmailAlreadyRegisteredError(email))` |

Hormis `ValidationError` et `ErrorCollection`, la librairie ne fournit aucun type d'erreur : les autres erreurs ci-dessus sont des `sealed record` du domaine dérivés de `Error`, qui surchargent `Category` (sinon `Technical`, HTTP 500).

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
    return await Result<UserEntity?>.TryAsync(
            async () => await _dbContext.Users.FindAsync([id], ct),
            ex => new DatabaseError(ex))
        .Bind(entity => entity is null
            ? Result.Failure<User>(new UserNotFoundError(id))
            : Result.Success(entity.ToDomain()));
}
```

## Exception mapper (couche anti-corruption)

Pour les librairies tierces qui throw:

```csharp
// ✅ Mapper les exceptions d'infra en Result à la frontière
public static class HttpClientResultExtensions
{
    public static async ValueTask<Result<T>> GetAsResultAsync<T>(
        this HttpClient client, 
        string url,
        CancellationToken ct = default)
    {
        try
        {
            return Result.Success(await client.GetFromJsonAsync<T>(url, ct)
                                  ?? throw new InvalidOperationException("Null response"));
        }
        catch (HttpRequestException http)
        {
            return Result.Failure<T>(new NetworkError(http.StatusCode, http.Message));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<T>(new TimeoutError(url));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<T>(new TechnicalError(ex.Message));
        }
    }
}
```

`TaskCanceledException` n'est un timeout que si le jeton de l'appelant n'est pas annulé ; l'annulation demandée par l'appelant se propage. `Try` / `TryAsync` ne conviennent pas ici : ils attrapent toute `Exception`, annulation comprise.

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
// ❌ BLOQUANT — GenericError : record fourre-tout (ErrorCode, message)
catch (SomePredictableException ex)
{
    return Result.Failure(new GenericError(ErrorCode.Failure, ex.Message)); // perd le typage fort
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
