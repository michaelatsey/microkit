# Rule: Result Patterns — MicroKit.Result

## Règles de création de Result

### ✅ Création via factory methods statiques
```csharp
Result ok = Result.Success();
Result<T> value = Result.Success(input);
Result failed = Result.Failure(error);
Result<T> failedValue = Result.Failure<T>(error);

// mapper : journalise l'exception et retourne une erreur au message générique
Result saved = Result.Try(() => riskyCommand(), mapper.Map);                                  // Func<Result>
Result<T> loaded = Result<T>.Try(() => riskyQuery(), mapper.Map);                             // Func<T>
Result savedAsync = await Result.TryAsync(async () => await riskyCommandAsync(), mapper.Map);  // Func<Task<Result>>
Result<T> loadedAsync = await Result<T>.TryAsync(async () => await riskyQueryAsync(), mapper.Map); // Func<Task<T>>
```

`DatabaseExceptionMapper` (`rules/csharp-style.md`) est un tel mapper. Le comportement actuel sans mapper, et la Phase 2 de #184 qui le change, sont décrits dans `rules/no-exceptions-flow.md`.

### ❌ Ne jamais créer via constructeur public
```csharp
// ❌ Interdit — les constructeurs de Result<T> sont privés
new Result<T>(value);
```

## Règles de consommation

### ✅ Toujours consommer via Match pour les flux de contrôle
```csharp
return result.Match(
    onSuccess: value => ProcessValue(value),
    onFailure: error => HandleError(error)
);
```

### ✅ Map pour transformer sans changer le rail
```csharp
Result<UserDto> dto = userResult.Map(u => new UserDto(u.Id, u.Name));
```

### ✅ Bind pour chaîner des opérations résultant en Result
```csharp
Result<Invoice> invoice = userResult
    .Bind(u => GetCart(u.Id))
    .Bind(c => CreateInvoice(c));
```

### ✅ Tap pour les side-effects (logs, events)
```csharp
internal static partial class UserLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} loaded")]
    public static partial void UserLoaded(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Operation failed: {Code}")]
    public static partial void OperationFailed(this ILogger logger, string code);
}

Result<User> withLog = userResult.Tap(u => logger.UserLoaded(u.Id));
```

### ✅ Ensure pour les validations inline
```csharp
Result<User> active = userResult
    .Ensure(u => u.IsActive, new UserInactiveError(userId))
    .Ensure(u => u.IsEmailVerified, new EmailNotVerifiedError());
```

### ✅ TapError pour gérer les side-effects sur l'échec
```csharp
Result<User> monitored = userResult
    .TapError(e => _metrics.RecordFailure(e.Code));
```

## Règles de combinaison

### ✅ Combine pour valider plusieurs résultats
```csharp
// Fail-fast: arrête au premier échec
Result failFast = ResultCombineExtensions.Combine(validateName, validateEmail, validateAge);

// Collect-all: collecte toutes les erreurs dans une ErrorCollection
Result collected = ResultCombineExtensions.CombineAll(validateName, validateEmail, validateAge);
```

## Règles de mapping HTTP

### ✅ ToHttpResult() pour les minimal APIs
```csharp
app.MapGet("/users/{id}", async (Guid id, IUserService svc) =>
    (await svc.GetUserAsync(id)).ToHttpResult());
```

### ✅ Mapping implicite des ErrorCategory vers HTTP
```
ErrorCategory.NotFound          → 404 Not Found
ErrorCategory.Validation        → 422 Unprocessable Entity  
ErrorCategory.Unauthorized      → 401 Unauthorized
ErrorCategory.Forbidden         → 403 Forbidden
ErrorCategory.Conflict          → 409 Conflict
ErrorCategory.TooManyRequests   → 429 Too Many Requests
ErrorCategory.Technical         → 500 Internal Server Error
ErrorCategory.Unavailable       → 503 Service Unavailable
ErrorCategory.External          → 502 Bad Gateway
ErrorCategory.BusinessRule      → 422 Unprocessable Entity
ErrorCategory.NotSupported      → 501 Not Implemented
ErrorCategory.Timeout           → 408 Request Timeout
ErrorCategory.Cancelled         → 499 Client Closed Request
ErrorCategory.PreconditionFailed → 412 Precondition Failed
toute autre valeur              → 500 Internal Server Error
```

`ErrorCategory.Timeout` répond aujourd'hui 408 ; #184 Phase 5 décide de son statut.

## Règles d'erreurs

### ✅ Une erreur = un type
```csharp
// ✅ Types distincts pour des erreurs distincts
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}

public sealed record UserInactiveError(Guid UserId)
    : Error(ErrorCode.From("USER.INACTIVE"), $"User {UserId} is inactive")
{
    public override ErrorCategory Category => ErrorCategory.BusinessRule;
}

// ❌ Pas de string discriminants. GenericError n'existe pas dans MicroKit.Result : c'est un type
// défini par l'application, fourre-tout à éviter
public sealed record GenericError(ErrorCode Code, string Message) : Error(Code, Message);

return Result.Failure(new GenericError(ErrorCode.From("USER_NOT_FOUND"), "..."));  // perd le typage fort
```

### ✅ ErrorCode en SCREAMING_SNAKE_CASE hiérarchique
```
DOMAIN.ENTITY.ACTION
AUTH.USER.NOT_FOUND
ORDER.PAYMENT.DECLINED
INVENTORY.PRODUCT.OUT_OF_STOCK
```

### ✅ Utiliser ErrorCollection pour les erreurs multiples
```csharp
var errors = ErrorCollection.From(error1, error2, error3);
return Result.Failure(errors);
```

## Règles async

### ✅ ValueTask pour les méthodes async à résultat souvent synchrone
```csharp
public ValueTask<Result<T>> GetAsync(Guid id, CancellationToken ct = default);
```

### ✅ ConfigureAwait(false) dans la librairie
```csharp
var user = await _repo.GetAsync(id, ct).ConfigureAwait(false);
```

### ✅ CancellationToken toujours en dernier paramètre avec default
```csharp
public async ValueTask<Result<T>> ProcessAsync(
    T input, 
    CancellationToken ct = default)  // ← toujours avec default
```

## Anti-patterns stricts

```csharp
// ❌ try/catch pour créer un Result dans le code métier ; GenericError (type applicatif) expose ex.Message au client
try { return Result.Success(Compute()); }
catch (Exception ex) { return Result.Failure<T>(new GenericError(ErrorCode.Failure, ex.Message)); }
// ✅ Utiliser Result<T>.Try() avec un mapper — Result.Try() n'accepte que Func<Result>
return Result<T>.Try(() => Compute(), mapper.Map);

// ❌ .Value sans vérification (sauf après Match)
var value = result.Value; // throw si failure
// ✅ Try pattern : l'erreur reste sur le rail d'échec
if (!result.TryGetValue(out var user))
    return Result.Failure<UserDto>(result.Error);
return Result.Success(user.ToDto());
// ✅ Valeur de repli choisie explicitement, quand l'échec a ce sens-là
string label = result.Map(u => u.Name).GetValueOrDefault("N/A");

// ❌ Result<Result<T>> 
Result<Result<User>> nested; // ← Bind manqué
// ✅
Result<User> flat = outer.Bind(inner => inner);

// ❌ void retourné là où Result suffit
void SaveUser(User user) { _repo.Save(user); }
// ✅
Result SaveUser(User user) => Result.Try(() => { _repo.Save(user); return Result.Success(); }, mapper.Map);
```
