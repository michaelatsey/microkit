---
name: ddd-errors
description: Use when designing, naming or reviewing a typed domain error for MicroKit.Result (error code, category, client-safe message, metadata), or translating errors between bounded contexts. Not for composing pipelines or for infrastructure exception handling.
---

# Skill: DDD Error Design

## Quand activer ce skill
- Conception ou nommage d'une nouvelle erreur de domaine
- Review d'erreurs existantes
- Traduction d'erreurs entre bounded contexts

## Ce qui atteint le client

`Error.Message` part dans `ProblemDetails.Detail` : il reste sans danger pour le client, et n'est jamais le texte d'une exception. Les détails internes vont dans le log, pas dans `Metadata` : `ProblemDetails` et `ResultJsonConverter<T>` ne l'émettent pas, ni le `Result` non générique retourné tel quel, et ADR-RESULT-001 D1 en fait un contexte en mémoire, jamais envoyé au client par défaut (garantie security, `.claude/CLAUDE.md`).

Un paramètre positionnel d'un record est une propriété publique : sérialiser l'erreur elle-même, ou l'afficher par `ToString`, l'émet. Un détail interne n'est donc jamais un paramètre de l'erreur.

Un id interpolé dans le message s'affiche par son `ToString`. Les ids de ce skill (`OrderId`, `ProductId`) le surchargent pour n'afficher que leur valeur, comme ceux de MicroKit.Domain ; sans cela, le message porte le texte généré du record (`OrderId { Value = … }`).

`Category` vaut `Technical` (HTTP 500) quand elle n'est pas surchargée : ADR-RESULT-001 D5 la garde facultative dans le code, et l'analyseur `MKR002` (`MicroKit.Result.Analyzers`) signale une sous-classe qui l'oublie.

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
    public override ErrorCategory Category => ErrorCategory.BusinessRule;
}
```

### Niveau 2: Erreurs d'application (orchestration)
Représentent des problèmes de workflow ou de coordination.

```csharp
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.NOT_FOUND"),
            $"User with ID {UserId} was not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
    public override ErrorSeverity Severity => ErrorSeverity.Information;
}

// Utilisateur identifié, opération refusée : Forbidden (403). Unauthorized (401) signifie
// « authentification requise » et appelle un en-tête WWW-Authenticate (RFC 9110, 15.5.2)
public sealed record OperationForbiddenError(string Operation, Guid UserId)
    : Error(ErrorCode.From("AUTH.OPERATION.FORBIDDEN"),
            $"User {UserId} is not allowed to perform '{Operation}'")
{
    public override ErrorCategory Category => ErrorCategory.Forbidden;
}
```

### Niveau 3: Erreurs d'infrastructure (frontière)
Créées par la frontière qui attrape l'exception (`rules/no-exceptions-flow.md`). Ce skill n'en conçoit que le message, générique et sans danger pour le client.

```csharp
// Aucun paramètre : la source et le détail technique partent dans le log, par [LoggerMessage],
// à la frontière qui crée l'erreur
public sealed record DatabaseUnavailableError()
    : Error(ErrorCode.From("INFRA.DATABASE.UNAVAILABLE"),
            "Database connection failed")
{
    public override ErrorCategory Category => ErrorCategory.Technical;
    public override ErrorSeverity Severity => ErrorSeverity.Critical;
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

// ❌ Erreur qui porte le texte d'une exception : ex.Message atteint le client
// (GenericError : type défini par l'application, déclaré dans rules/result-patterns.md)
new GenericError(ErrorCode.Failure, ex.Message);
// ✅ Erreur typée, dont le message ne dit que ce que le client peut savoir
new EntityAlreadyExistsError(entityType, entityId);
```

Où et comment attraper l'exception qui conduit à ces erreurs relève de `rules/no-exceptions-flow.md`.

## Traduction entre bounded contexts

```csharp
// Les erreurs ne doivent pas traverser les frontières de BC directement
// Translator pattern:

public static class OrderErrorTranslator
{
    public static IError Translate(IError paymentError) => paymentError switch
    {
        PaymentDeclinedError e => new OrderPaymentFailedError(e.OrderId, e.Reason),
        PaymentGatewayTimeoutError => new OrderProcessingDelayedError(),
        PaymentValidationError => new OrderPaymentRejectedError(),   // reste une erreur 422
        _ => new OrderTechnicalError()  // ni le code ni le message du BC Payment ne traversent
    };
}
```

Le repli efface la catégorie de l'erreur d'origine : une erreur que l'appelant doit distinguer (validation, refus) se traduit par sa propre branche. L'appelant journalise l'erreur d'origine avant de la traduire, par `[LoggerMessage]`.

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
        yield return new("error.severity", error.Severity.ToString());
    }
}
```

`Metadata` n'est pas exporté : ADR-RESULT-001 D1 en fait un contexte en mémoire, et une trace part souvent vers un service tiers.
