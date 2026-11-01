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
| Infrastructure non disponible (DB down) | Attraper à la frontière + erreur `ErrorCategory.Technical` au message générique (ci-dessous) |

## Frontière infrastructure

Les appels d'infrastructure (DB, HTTP, files) doivent être wrappés. Une erreur technique porte un message générique, sans danger pour le client ; l'exception part dans le log :

```csharp
public sealed record DatabaseError()
    : Error(ErrorCode.From("INFRA.DATABASE.FAILURE"), "A database error occurred.");

internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database call failed")]
    public static partial void DatabaseCallFailed(this ILogger logger, Exception exception);
}

// ✅ Pattern pour les appels d'infra
public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)
{
    UserEntity? entity;
    try
    {
        entity = await _dbContext.Users.FindAsync([id], ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw; // annulation demandée par l'appelant : elle se propage
    }
    catch (Exception ex)
    {
        _logger.DatabaseCallFailed(ex);
        return Result.Failure<User>(new DatabaseError());
    }

    return entity is null
        ? Result.Failure<User>(new UserNotFoundError(id))
        : Result.Success(entity.ToDomain());
}
```

## Exception mapper (couche anti-corruption)

Pour les librairies tierces qui throw:

```csharp
// Erreurs applicatives : messages génériques, jamais le texte d'une exception
public sealed record RemoteCallError(HttpStatusCode? StatusCode)
    : Error(ErrorCode.ExternalServiceFailure, "The remote service call failed.")
{
    public override ErrorCategory Category => ErrorCategory.External;
}

public sealed record RemoteTimeoutError()
    : Error(ErrorCode.Timeout, "The remote service did not respond in time.")
{
    public override ErrorCategory Category => ErrorCategory.Timeout;
}

public sealed record TechnicalError()
    : Error(ErrorCode.Internal, "An internal error occurred.");

// ✅ Mapper les exceptions d'infra en Result à la frontière
public static partial class HttpClientResultExtensions
{
    public static async ValueTask<Result<T>> GetAsResultAsync<T>(
        this HttpClient client,
        string url,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            T? value = await client.GetFromJsonAsync<T>(url, ct).ConfigureAwait(false);
            return value is null
                ? Result.Failure<T>(new RemoteCallError(null))
                : Result.Success(value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // annulation demandée par l'appelant : elle se propage
        }
        catch (TaskCanceledException ex) when (ex is { InnerException: TimeoutException })
        {
            logger.RemoteCallFailed(ex, url);
            return Result.Failure<T>(new RemoteTimeoutError());
        }
        catch (HttpRequestException ex)
        {
            logger.RemoteCallFailed(ex, url);
            return Result.Failure<T>(new RemoteCallError(ex.StatusCode));
        }
        catch (Exception ex)
        {
            logger.RemoteCallFailed(ex, url);
            return Result.Failure<T>(new TechnicalError());
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Remote call to {Url} failed")]
    private static partial void RemoteCallFailed(this ILogger logger, Exception exception, string url);
}
```

L'annulation demandée par l'appelant se propage : on attrape `OperationCanceledException` filtrée sur `ct.IsCancellationRequested`, ou rien du tout, jamais `TaskCanceledException` seule (Microsoft Learn, *Best practices for exceptions*). Un timeout d'`HttpClient` s'identifie par `TaskCanceledException { InnerException: TimeoutException }` (Microsoft Learn, *Make HTTP requests with the HttpClient class*).

## `Try` / `TryAsync`

Ils ne conviennent pas à un appel annulable, et s'emploient toujours avec un mapper qui journalise l'exception et retourne une erreur au message générique (`DatabaseExceptionMapper`, `rules/csharp-style.md`). Comportement actuel, que #184 Phase 2 change :

- Toutes les surcharges attrapent `OperationCanceledException`. La Phase 2 fait relancer celles sans mapper ; avec un mapper, le mapper décide.
- Sans mapper, le message de l'erreur est `Exception.Message`, et `ResultProblemDetailsFactory` l'écrit dans `ProblemDetails.Detail`. La Phase 2 le remplace par un message générique.

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
// ❌ BLOQUANT — GenericError : type défini par l'application, déclaré dans rules/result-patterns.md
catch (SomePredictableException ex)
{
    return Result.Failure(new GenericError(ErrorCode.Failure, ex.Message)); // perd le typage fort, expose ex.Message au client
}

// ❌ BLOQUANT  
public async Task<User> GetUserAsync(Guid id)
{
    var user = await _repo.FindAsync(id);
    if (user is null) throw new NotFoundException(id); // prévisible!
    return user;
}

// ❌ BLOQUANT
public Result<Order> GetOrder(Guid id)
{
    Order? order = _repo.Find(id);
    if (order is null) throw new InvalidOperationException("Order not found"); // prévisible : new OrderNotFoundError(id)
    return Result.Success(order);
}
```

`Result<T>.Value`, `Error` et `GetValueOrThrow` lèvent `ResultException`, et ce n'est pas un flux métier : c'est ce qui empêche de lire un résultat invalide comme un succès.
