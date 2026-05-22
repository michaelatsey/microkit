# Rule: Result Patterns — MicroKit.Result

## Règles de création de Result

### ✅ Création via factory methods statiques
```csharp
Result.Success();
Result.Success(value);
Result.Failure(error);
Result.Failure<T>(error);
Result.Try(() => riskyOperation());
Result.TryAsync(async () => await riskyOperation());
```

### ❌ Ne jamais créer via constructeur public
```csharp
// ❌ Interdit — le constructeur est internal
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
Result<User> withLog = userResult.Tap(u => _logger.LogInformation("User {Id}", u.Id));
```

### ✅ Ensure pour les validations inline
```csharp
Result<User> active = userResult
    .Ensure(u => u.IsActive, new UserInactiveError())
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
var result = Result.Combine(validateName, validateEmail, validateAge);

// Collect-all: collecte toutes les erreurs
var result = Result.CombineAll(validateName, validateEmail, validateAge);
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
```

## Règles d'erreurs

### ✅ Une erreur = un type
```csharp
// ✅ Types distincts pour des erreurs distincts
public sealed record UserNotFoundError(Guid UserId) : Error(...);
public sealed record UserInactiveError(Guid UserId) : Error(...);

// ❌ Pas de string discriminants
Result.Failure(new Error("USER_NOT_FOUND", "..."));  // perd le typage fort
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
// ❌ try/catch pour créer un Result dans le code métier
try { return Result.Success(Compute()); }
catch (Exception ex) { return Result.Failure(new Error(ex.Message)); }
// ✅ Utiliser Result.Try()
return Result.Try(() => Compute());

// ❌ .Value sans vérification (sauf après Match)
var value = result.Value; // throw si failure
// ✅ 
var value = result.Match(v => v, _ => default);

// ❌ Result<Result<T>> 
Result<Result<User>> nested; // ← Bind manqué
// ✅
Result<User> flat = outer.Bind(inner => inner);

// ❌ void retourné là où Result suffit
void SaveUser(User user) { _repo.Save(user); }
// ✅
Result SaveUser(User user) => Result.Try(() => _repo.Save(user));
```
