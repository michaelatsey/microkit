# Command: /new-error

## Usage
```
/new-error <ErrorName> [--category <ErrorCategory>] [--domain <DomainName>] [--fields <field1:type,field2:type>]
```

`<ErrorCategory>` : `NotFound`, `Validation`, `Unauthorized`, `Forbidden`, `Conflict`, `TooManyRequests`, `Technical`, `Unavailable`, `External`, `BusinessRule`, `NotSupported`, `Timeout`, `PreconditionFailed`. `Cancelled` n'est pas proposée : l'annulation demandée par l'appelant se propage, elle ne devient pas une erreur. Sans `--category`, la catégorie est `Technical` (HTTP 500) ; #184 Phase 3 décide si elle devient obligatoire.

## Description
Génère un type d'erreur fortement typé, cohérent avec les conventions MicroKit.Result.

## Exemples
```
/new-error UserNotFound --category NotFound --domain User --fields userId:Guid
/new-error OrderAmountInvalid --category Validation --domain Order --fields amount:decimal,minAmount:decimal
/new-error PaymentGatewayTimeout --category Timeout --domain Payment
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
    
    /// <inheritdoc/>
    public override ErrorSeverity Severity => ErrorSeverity.{Severity};
}
```

### Avec métadonnées
```csharp
public sealed record {ErrorName}({Fields}) 
    : Error(ErrorCode.From("..."), "...")
{
    public override ErrorCategory Category => ErrorCategory.{Category};
    
    // champs comme metadata pour observabilité
    public override IReadOnlyDictionary<string, object?> Metadata =>
        ErrorMetadata.Create(nameof({Field1}), {Field1});
}
```

## Règles appliquées automatiquement
1. `sealed record` — pas d'héritage non contrôlé
2. ErrorCode au format `DOMAIN.ENTITY.ACTION` en SCREAMING_SNAKE_CASE
3. Message sans danger pour le client : il part dans `ProblemDetails.Detail`. Jamais le texte d'une exception, ni un détail interne (requête, chemin, serveur) ; ceux-là vont dans `Metadata` ou le log. Une erreur `Technical` porte un message générique
4. Category cohérente avec le nom (NotFound → category NotFound)
5. XML docs générés
6. Placement dans le bon namespace/dossier

## Output
- Fichier `{ErrorName}.cs` dans `Errors/` du bon domaine
- Test unitaire `{ErrorName}Tests.cs` de base (equality, metadata)
