# Rule: Result Patterns — MicroKit.Result

## Règles de création de Result

### ✅ Création via factory methods statiques
```csharp
Result ok = Result.Success();
Result<T> value = Result.Success(input);
Result failed = Result.Failure(error);
Result<T> failedValue = Result.Failure<T>(error);

// mapper : journalise l'exception et retourne une erreur au message générique.
// Les appels ne prennent pas de CancellationToken, et le lambda n'en capture pas.
Result saved = Result.Try(() => riskyCommand(), mapper.Map);                                  // Func<Result>
Result<T> loaded = Result<T>.Try(() => riskyQuery(), mapper.Map);                             // Func<T>
Result savedAsync = await Result.TryAsync(async () => await riskyCommandAsync(), mapper.Map);  // Func<Task<Result>>
Result<T> loadedAsync = await Result<T>.TryAsync(async () => await riskyQueryAsync(), mapper.Map); // Func<Task<T>>
```

`DatabaseExceptionMapper` (`rules/csharp-style.md`) est un tel mapper. Il fait une erreur de toute exception, `OperationCanceledException` comprise : un appel annulable ne passe pas par `Try` / `TryAsync` mais par un `catch` à la frontière (`rules/no-exceptions-flow.md`), qui laisse l'annulation se propager. Sans mapper, `Try` / `TryAsync` relancent `OperationCanceledException` et font de toute autre exception une `ExceptionError` au message générique (`rules/no-exceptions-flow.md`).

### ❌ Ne jamais créer via constructeur
```csharp
// ❌ new Result<T>(value) ne compile pas : Result<T> n'a pas de constructeur public à argument
// ❌ new Result<T>() compile, et donne l'état non initialisé de default : ni succès ni échec
Result<T> invalid = new Result<T>();
```

## Règles de consommation

### ✅ Consommer via Match, ou TryGetValue avec retour anticipé (anti-patterns ci-dessous)
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

`Tap` ne transforme pas : il ne modifie ni la valeur ni l'objet qu'elle référence. Une transformation passe par `Map` ou `Bind`, qui retournent la nouvelle valeur.

```csharp
// ❌ Tap qui modifie la valeur
Result<User> mutated = userResult.Tap(u => u.Rename(newName));
// ✅
Result<User> renamed = userResult.Map(u => u.WithName(newName));
```

Un effet async passe par `Tap`, `TapError` ou `Finally` avec un lambda async, ou par `TapAsync`, `TapErrorAsync` ou `FinallyAsync` : la surcharge prise attend l'effet et retourne un `ValueTask`, qu'on attend. Une `Action` garde la surcharge synchrone.

### ✅ Ensure pour les validations inline
```csharp
Result<User> active = userResult
    .Ensure(u => u.IsActive, new UserInactiveError(userId))
    .Ensure(u => u.IsEmailVerified, new EmailNotVerifiedError());
```

`Ensure` reçoit l'erreur déjà construite : elle l'est à chaque appel, succès compris, avec son message interpolé. Quand la construire coûte, `Bind` ne la construit que sur l'échec :

```csharp
Result<User> activeLazy = userResult
    .Bind(u => u.IsActive ? Result.Success(u) : Result.Failure<User>(new UserInactiveError(u.Id)));
```

### ✅ MapError et Compensate sur le rail d'échec
```csharp
// MapError : transformer l'erreur, sans changer de rail
Result<Order> enriched = orderResult.MapError(e => new EnrichedError(e, correlationId));
// Compensate : revenir sur le rail de succès par une valeur de repli
Result<Product> product = productResult.Compensate(_ => GetDefaultProduct());
```

Une erreur qui en enveloppe une autre reprend sa `Category`, sa `Severity` et ses `Metadata` ; sinon elle vaut `Technical` et répond 500 (`samples/MicroKit.Result.Samples/Guide/Composing.cs`).

### ✅ TapError pour gérer les side-effects sur l'échec
```csharp
Result<User> monitored = userResult
    .TapError(e => _metrics.RecordFailure(e.Code));
```

## Règles de combinaison

### ✅ Combine pour valider plusieurs résultats
```csharp
// Fail-fast : retourne le premier échec ; les arguments sont tous évalués avant l'appel
Result failFast = Result.Combine(validateName, validateEmail, validateAge);

// Collect-all: collecte toutes les erreurs dans une ErrorCollection
Result collected = Result.CombineAll(validateName, validateEmail, validateAge);
```

Pour qu'une étape ne s'exécute pas après un échec, `Bind` la chaîne : `Combine(Validate(x), Save(x))` exécute `Save`. Sur un résultat `default`, `Combine` et `CombineAll` lèvent `ResultException`. Ce sont des membres statiques de `Result` (ADR-RESULT-001 D7).

## `Result` non générique

Il a les opérations de `Result<T>`, sync et async (ADR-RESULT-001 D9) : `Map` vers un `Result<TOut>`, `Bind` vers un `Result` ou un `Result<TOut>`, `Ensure`, `MapError`, `Compensate`, `Match`, `Tap`, `TapError`, `Finally`. `Result<T>.Bind(Func<T, Result>)` mène d'une valeur à une commande.

```csharp
Result saved = GetUser(id).Bind(user => Save(user));
Result<Invoice> invoice = Validate(command).Bind(() => CreateInvoice(command));
```

## Règles de mapping HTTP

### ✅ ToHttpResult() pour les minimal APIs
```csharp
app.MapGet("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
    (await svc.GetUserAsync(id, ct)).ToHttpResult());
```

Le `CancellationToken` d'un endpoint minimal API est celui de la requête, annulé quand le client abandonne.

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
ErrorCategory.Timeout           → 504 Gateway Timeout
ErrorCategory.Cancelled         → 499 Client Closed Request
ErrorCategory.PreconditionFailed → 412 Precondition Failed
toute autre valeur              → 500 Internal Server Error
```

L'annulation demandée par l'appelant se propage (`rules/no-exceptions-flow.md`) : elle ne devient jamais une erreur `ErrorCategory.Cancelled`.

Une erreur de validation, seule ou dans une `ErrorCollection`, répond en `HttpValidationProblemDetails` : `errors` regroupe ses messages par `PropertyName` (`{ "Email": ["..."] }`) ; seule une réponse `Validation` ou `BusinessRule` en porte, jamais une erreur d'accès, de ressource ou du système. Une `ErrorCollection` liste le code de chacune de ses erreurs dans `errorCodes` ; sur une réponse d'une autre catégorie que `Validation` ou `BusinessRule`, `detail` et `errorCodes` ne nomment que ses erreurs de la catégorie retenue : un appelant non authentifié ne reçoit ni message, ni code, ni nom de propriété de validation. Cela vaut pour l'`ErrorCollection` elle-même : une erreur qui l'enveloppe et reprend son `Message` (`EnrichedError`) est une erreur seule, dont `detail` porte tous les messages de la collection, ceux de validation compris. Une `ErrorCollection` répond avec la catégorie que partagent ses erreurs ; sinon avec celle de plus haute préséance (`.claude/CLAUDE.md`, garantie interoperability), quel que soit leur ordre : un 500 ne se cache pas derrière un 422, ni un 401 derrière un 422.

`ToHttpResult`, `ToCreatedHttpResult(location)` et `ToAcceptedHttpResult(location)` retournent des `TypedResults` (`Results<Ok<T>, ProblemHttpResult>`…) : OpenAPI voit le type du succès ; les réponses problem se déclarent par `ProducesProblem(status)`. Les problem details passent par l'`IProblemDetailsService` de l'hôte (`AddProblemDetails`), donc `CustomizeProblemDetails` et le `traceId` s'appliquent ; `type` est la section qu'ASP.NET Core connaît pour le statut (RFC 9110, RFC 4918 pour 422), absente pour 429 et 499. `Timeout` répond 504 : l'opération, typiquement un appel à une dépendance, n'a pas abouti à temps ; 408 accuserait la requête du client.

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

Le code et les samples emploient aussi deux segments (`USER.NOT_FOUND`). ADR-RESULT-001 D1 fixe son rôle : identifiant stable, chaîne libre, `DOMAIN.ENTITY.REASON` par convention.

### ✅ Utiliser ErrorCollection pour les erreurs multiples
```csharp
var errors = ErrorCollection.From(error1, error2, error3);
return Result.Failure(errors);
```

`WithCategory`, `WithSeverity`, `WithCode` et `OfType` retournent une liste, vide quand rien ne correspond : une `ErrorCollection` n'est jamais vide. `Flatten` retourne une `ErrorCollection`.

## Règles async

### ✅ ValueTask<T> pour l'async (convention racine)
```csharp
public ValueTask<Result<T>> GetAsync(Guid id, CancellationToken ct = default);
```

Son coût face à `Task<T>` est mesuré dans `README.md` (Performance) : plus rapide et plus petit en complétion synchrone, un peu plus lent et plus gros quand l'opération cède vraiment.

### ✅ Un ValueTask s'attend directement, une seule fois (CA2012)
```csharp
// ✅
Result<User> user = await GetUserAsync(id, ct);

// ❌ Bloque, et lit un résultat peut-être pas encore disponible
Result<User> blocked = GetUserAsync(id, ct).Result;
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

La règle vaut pour le code qui appelle la librairie et pour une nouvelle méthode qui fait elle-même un appel annulable. Chaque extension async a deux formes (ADR-RESULT-001 D8) : un délégué qui retourne une `Task`, sans jeton, et un délégué qui prend le `CancellationToken` et retourne un `ValueTask`, avec `CancellationToken ct = default` en dernier, transmis au délégué :

```csharp
Result<Order> order = await userResult.BindAsync((user, ct) => orders.GetLatestAsync(user.Id, ct), ct);
```

## Anti-patterns stricts

```csharp
// ❌ try/catch pour créer un Result dans le code métier ; GenericError (type applicatif) expose ex.Message au client
try { return Result.Success(Compute()); }
catch (Exception ex) { return Result.Failure<T>(new GenericError(ErrorCode.Failure, ex.Message)); }
// ✅ Utiliser Result<T>.Try() avec un mapper — Result.Try() n'accepte que Func<Result>
return Result<T>.Try(() => Compute(), mapper.Map);

// ❌ .Value sans vérification préalable du succès
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
