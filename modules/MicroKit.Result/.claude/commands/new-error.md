# Command: /new-error

## Usage
```
/new-error <ErrorName> [--category <ErrorCategory>] [--severity <ErrorSeverity>] [--domain <DomainName>] [--fields <field1:type,field2:type>]
```

`<ErrorName>` finit par `Error` (`.claude/CLAUDE.md`) ; la commande l'ajoute s'il manque.

`<ErrorCategory>` : `NotFound`, `Unauthorized`, `Forbidden`, `Conflict`, `TooManyRequests`, `Technical`, `Unavailable`, `External`, `BusinessRule`, `NotSupported`, `Timeout`, `PreconditionFailed`. `Cancelled` n'est pas proposée : l'annulation demandée par l'appelant se propage, elle ne devient pas une erreur.

Sans `--category`, la catégorie se déduit du nom (règle 4 ci-dessous) ; si le nom ne la donne pas, la commande la demande. Le template surcharge toujours `Category` : sans surcharge, `Error.Category` vaut `Technical` (HTTP 500), et ADR-RESULT-001 D5 la garde facultative : la Phase 6 ajoute un avertissement d'analyseur pour une sous-classe qui l'oublie.

`Validation` n'est pas proposée non plus : une erreur de validation est une `ValidationError` (`ValidationError.Custom(propertyName, message)` ou ses factories), qui implémente `IValidationError`. `ValidationResult.AddError` a deux surcharges : `AddError(IValidationError error)`, et `AddError(string propertyName, string message)`, qui construit une `ValidationError`. L'une comme l'autre portent un `PropertyName` : `ToHttpResult()` regroupe les messages par champ dans `errors`.

`<ErrorSeverity>` : `Information`, `Warning`, `Error`, `Critical`. Sans `--severity`, le template ne surcharge pas `Severity`, qui vaut `Error`.

## Description
Génère un type d'erreur fortement typé, cohérent avec les conventions MicroKit.Result.

## Exemples
```
/new-error UserNotFoundError --category NotFound --domain User --fields userId:Guid
/new-error OrderAmountTooHighError --category BusinessRule --domain Order --fields amount:decimal,maxAmount:decimal
/new-error PaymentGatewayTimeoutError --category Timeout --severity Warning --domain Payment
```

## Template généré

### Erreur simple
```csharp
namespace {RootNamespace};

/// <summary>
/// Raised when {description}.
/// </summary>
/// <param name="{Field1}">{Field1 description}.</param>
public sealed record {ErrorName}({Type1} {Field1}, ...)
    : Error(
        ErrorCode.From("{DOMAIN}.{ENTITY}.{ACTION}"),
        $"[description sans danger pour le client]")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.{Category};
    
    // seulement avec --severity
    /// <inheritdoc/>
    public override ErrorSeverity Severity => ErrorSeverity.{Severity};
}
```

### Avec métadonnées
```csharp
/// <summary>
/// Raised when {description}.
/// </summary>
/// <param name="{Field1}">{Field1 description}.</param>
public sealed record {ErrorName}({Fields}) 
    : Error(ErrorCode.From("..."), "...")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.{Category};
    
    // Champs sans danger pour un client seulement : un champ positionnel est déjà une propriété
    // publique, et ADR-RESULT-001 D1 : Metadata reste en mémoire
    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata =>
        ErrorMetadata.Create(nameof({Field1}), {Field1});
}
```

## Règles appliquées automatiquement
1. `sealed record` — pas d'héritage non contrôlé
2. ErrorCode au format `DOMAIN.ENTITY.ACTION` en SCREAMING_SNAKE_CASE
3. Message sans danger pour le client : il part dans `ProblemDetails.Detail`. Jamais le texte d'une exception, ni un détail interne (requête, chemin, serveur) ; ceux-là vont dans le log, jamais dans le message ni dans un champ de l'erreur. Une erreur `Technical` porte un message générique
4. Category cohérente avec le nom (`…NotFoundError` → `NotFound`, `…ForbiddenError` → `Forbidden`, `…TimeoutError` → `Timeout`)
5. XML docs générés
6. Placement dans le bon namespace/dossier

## Output
- Fichier `{ErrorName}.cs` dans `Errors/` du bon domaine
- Test unitaire `{ErrorName}Tests.cs` de base (equality, metadata)
