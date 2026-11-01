# Skill: DDD Error Design

## Quand activer ce skill
- Conception d'une nouvelle erreur métier
- Review d'erreurs existantes
- Mapping entre domaines / bounded contexts
- Traduction d'erreurs vers l'API externe

## Taxonomie des erreurs DDD

### Niveau 1: Erreurs de domaine (les plus importantes)
Représentent des violations de règles métier ou d'invariants.

```csharp
// Violation d'invariant
public sealed record OrderAlreadyShippedError(OrderId OrderId, DateTimeOffset ShippedAt)
    : Error(ErrorCode.From("ORDER.INVARIANT.ALREADY_SHIPPED"), 
            $"Order {OrderId} was already shipped at {ShippedAt:O}")
{
    public override ErrorCategory Category => ErrorCategory.Conflict;
    public override ErrorSeverity Severity => ErrorSeverity.Warning;
}

// Règle métier
public sealed record InsufficientInventoryError(
    ProductId ProductId, 
    int Requested, 
    int Available)
    : Error(ErrorCode.From("INVENTORY.STOCK.INSUFFICIENT"),
            $"Product {ProductId}: requested {Requested}, available {Available}")
{
    public override ErrorCategory Category => ErrorCategory.Conflict;
}
```

### Niveau 2: Erreurs d'application (orchestration)
Représentent des problèmes de workflow ou de coordination.

```csharp
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.QUERY.NOT_FOUND"),
            $"User with ID {UserId} was not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
    public override ErrorSeverity Severity => ErrorSeverity.Information;
}

public sealed record UnauthorizedOperationError(string Operation, Guid UserId)
    : Error(ErrorCode.From("AUTH.OPERATION.UNAUTHORIZED"),
            $"User {UserId} is not authorized to perform '{Operation}'")
{
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}
```

### Niveau 3: Erreurs d'infrastructure (frontière)
Capturées à la frontière, converties en erreurs techniques.

```csharp
public sealed record DatabaseUnavailableError(string DataSource, string Details)
    : Error(ErrorCode.From("INFRA.DATABASE.UNAVAILABLE"),
            "Database connection failed")
{
    public override ErrorCategory Category => ErrorCategory.Technical;
    public override ErrorSeverity Severity => ErrorSeverity.Critical;
    
    // Les détails techniques ne sont PAS dans le message public
    // Ils sont en metadata pour les logs internes
    public override IReadOnlyDictionary<string, object?> Metadata =>
        new Dictionary<string, object?>
        {
            ["dataSource"] = DataSource,
            ["details"] = Details
        };
}
```

## Conventions de naming

### ErrorCode format
```
{BOUNDED_CONTEXT}.{AGGREGATE}.{VIOLATION}

AUTH.USER.NOT_FOUND          ← BC=Auth, Aggregate=User, Violation=NotFound
ORDER.PAYMENT.DECLINED       ← BC=Order, Aggregate=Payment, Violation=Declined  
INVENTORY.PRODUCT.INACTIVE   ← BC=Inventory, Aggregate=Product, Violation=Inactive
NOTIFICATION.EMAIL.SEND_FAILED ← BC=Notification, Aggregate=Email, Violation=SendFailed
```

### Noms d'erreurs
```
{Entity}{Violation}Error

UserNotFoundError
OrderAlreadyShippedError
PaymentDeclinedError
ProductOutOfStockError
CartEmptyError
EmailAlreadyRegisteredError
```

## Anti-patterns DDD

```csharp
// ❌ Erreur trop générique — perd la sémantique
public sealed record OperationFailedError(string Message) : Error(ErrorCode.Failure, Message);

// ❌ Héritage profond — complexifie inutilement
// SpecificUserError → UserError → DomainError → Error
// ✅ Plat — toutes les erreurs héritent directement de Error
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}

// ❌ Mutable metadata
public Dictionary<string, object?> Metadata { get; set; } = new();
// ✅ Immutable : override de la propriété virtuelle Error.Metadata
public override IReadOnlyDictionary<string, object?> Metadata =>
    ErrorMetadata.Create("userId", UserId);

// ❌ Exception convertie sans enrichissement, ex.Message exposé au client
// (GenericError : type défini par l'application, déclaré dans rules/result-patterns.md)
catch (SqlException ex) 
{ return Result.Failure(new GenericError(ErrorCode.Failure, ex.Message)); }
// ✅ Erreur typée avec contexte
catch (SqlException ex) when (ex.Number == 2627) // unique constraint
{ return Result.Failure(new EntityAlreadyExistsError(entityType, entityId)); }
```

## Traduction entre bounded contexts

```csharp
// Les erreurs ne doivent pas traverser les frontières de BC directement
// Translator pattern:

public sealed class OrderErrorTranslator
{
    public static IError Translate(IError paymentError) => paymentError switch
    {
        PaymentDeclinedError e => new OrderPaymentFailedError(e.OrderId, e.Reason),
        PaymentGatewayTimeoutError => new OrderProcessingDelayedError(),
        _ => new OrderTechnicalError(paymentError.Code)
    };
}
```

## Erreurs et observabilité

```csharp
// Tags d'Activity pour les traces distribuées, calculés depuis l'API d'IError
// (extension côté application : Error n'expose pas GetActivityTags)
public static class ErrorActivityExtensions
{
    public static IEnumerable<KeyValuePair<string, object?>> GetActivityTags(this IError error)
    {
        yield return new("error.code", error.Code.Value);
        yield return new("error.category", error.Category.ToString());
        foreach (var (key, value) in error.Metadata)
            yield return new($"error.{key}", value);
    }
}
```
