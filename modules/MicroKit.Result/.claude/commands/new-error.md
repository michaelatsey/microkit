# Command: /new-error

## Usage
```
/new-error <ErrorName> [--category <NotFound|Validation|Conflict|Unauthorized|Technical>] [--domain <DomainName>] [--fields <field1:type,field2:type>]
```

## Description
Génère un type d'erreur fortement typé, cohérent avec les conventions MicroKit.Result.

## Exemples
```
/new-error UserNotFound --category NotFound --domain User --fields userId:Guid
/new-error OrderAmountInvalid --category Validation --domain Order --fields amount:decimal,minAmount:decimal
/new-error PaymentGatewayTimeout --category Technical --domain Payment
```

## Template généré

### Erreur simple
```csharp
namespace MicroKit.Domain.{Domain}.Errors;

/// <summary>
/// Raised when {description}.
/// </summary>
/// <param name="{Field1}">{Field1 description}.</param>
public sealed record {ErrorName}({Type1} {Field1}, ...)
    : Error(
        code: ErrorCode.From("{DOMAIN}.{ENTITY}.{ACTION}"),
        message: $"[description avec interpolation des champs]")
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
    
    public override IReadOnlyDictionary<string, object?> Metadata => 
        new Dictionary<string, object?>
        {
            // champs comme metadata pour observabilité
            [nameof({Field1})] = {Field1},
        };
}
```

## Règles appliquées automatiquement
1. `sealed record` — pas d'héritage non contrôlé
2. ErrorCode au format `DOMAIN.ENTITY.ACTION` en SCREAMING_SNAKE_CASE
3. Message avec interpolation des champs pour le debugging
4. Category cohérente avec le nom (NotFound → category NotFound)
5. XML docs générés
6. Placement dans le bon namespace/dossier

## Output
- Fichier `{ErrorName}.cs` dans `Errors/` du bon domaine
- Test unitaire `{ErrorName}Tests.cs` de base (equality, metadata)
