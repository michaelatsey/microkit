# Command: /new-value-object

## Usage
```
/new-value-object <Name> [--fields <field1:type,field2:type>] [--validate]
```

## Exemples
```
/new-value-object Money --fields "amount:decimal,currency:string" --validate
/new-value-object Email --fields "value:string" --validate
/new-value-object Address --fields "street:string,city:string,country:string"
```

## Ce qui est généré

### Recommandé — sealed record (moderne)
```csharp
/// <summary>[Description].</summary>
public sealed record {Name}
{
    public {Type} {Field} { get; }
    // autres propriétés...

    public {Name}({Types} {fields})
    {
        // validation si --validate
        Validate({fields});
        {Field} = {field};
    }

    private static void Validate(...)
    {
        // ArgumentException ou DomainException selon la violation
    }
}
```

### Alternatif — ValueObject avec GetEqualityComponents (si héritage requis)
```csharp
public sealed class {Name} : ValueObject
{
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return {Field1};
        yield return {Field2};
    }
}
```

### Tests générés
```csharp
public sealed class {Name}Tests
{
    [Fact] public void TwoInstances_WithSameValues_AreEqual()
    [Fact] public void TwoInstances_WithDifferentValues_AreNotEqual()
    [Fact] public void Create_WithInvalidData_ThrowsException() // si --validate
}
```

---

# Command: /new-specification

## Usage
```
/new-specification <Name> [--entity <EntityType>] [--description "<what it checks>"]
```

## Exemples
```
/new-specification ActiveUser --entity User --description "User is active and email verified"
/new-specification OverdueOrder --entity Order --description "Order not shipped after 7 days"
```

## Ce qui est généré

```csharp
/// <summary>
/// Specification that checks: {description}.
/// </summary>
public sealed class {Name}Specification : Specification<{Entity}>
{
    public override Expression<Func<{Entity}, bool>> ToExpression()
        => entity => /* TODO: condition */;
}
```

### Tests
```csharp
public sealed class {Name}SpecificationTests
{
    [Fact] public void IsSatisfiedBy_WhenConditionMet_ReturnsTrue()
    [Fact] public void IsSatisfiedBy_WhenConditionNotMet_ReturnsFalse()
    [Fact] public void ToExpression_CanBeComposedWithAnd()
    [Fact] public void ToExpression_IsCompatibleWithLinqWhere()
}
```

---

# Command: /gen-domain-tests

## Usage
```
/gen-domain-tests <FilePath> [--arch]
```

## Description
Génère les tests manquants pour un type de domaine (aggregate, VO, spec, rule).
Avec `--arch` : génère aussi les tests d'architecture NetArchTest.

## Exemples
```
/gen-domain-tests src/MicroKit.Domain/Aggregates/AggregateRoot.cs
/gen-domain-tests src/MicroKit.Domain/ --arch
```

## Cas inférés automatiquement

| Pattern dans le code | Test généré |
|---|---|
| `CheckRule(new XRule(...))` | `ThrowBusinessRuleViolation_When{Condition}` |
| `RaiseDomainEvent(new XEvent(...))` | `Raise{X}Event_When{Condition}` |
| `PopDomainEvents()` | `PopDomainEvents_ClearsEventsAfterCall` |
| `ArgumentNullException.ThrowIfNull` | `Create_WithNullArg_ThrowsArgumentNull` |
| `sealed record` VO | égalité structurelle testée |
| `ISpecification<T>` | `IsSatisfiedBy` + `ToExpression` testés |
