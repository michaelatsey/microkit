# Command: /new-aggregate

## Usage
```
/new-aggregate <AggregateName> [--id <IdType|new>] [--events <Event1,Event2>] [--rules <Rule1,Rule2>]
```

## Description
Génère un AggregateRoot complet avec son ID fortement typé, ses DomainEvents,
ses BusinessRules et son test skeleton.

## Exemples
```
/new-aggregate Order --id OrderId --events OrderPlaced,OrderShipped,OrderCancelled --rules OrderMustHaveItems,OrderCannotBeShippedIfCancelled
/new-aggregate Customer --id new --events CustomerRegistered
```

## Ce qui est généré

### ID (si --id new ou type inexistant)
```csharp
public readonly record struct {Name}Id(Guid Value) : IEntityId
{
    public static {Name}Id New() => new(Guid.NewGuid());
    public static {Name}Id Empty => new(Guid.Empty);
    public static {Name}Id From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

### AggregateRoot
```csharp
public sealed class {Name} : AggregateRoot<{Name}Id>
{
    // Private constructor — factory method obligatoire
    private {Name}({Name}Id id, ...) : base(id) { }

    // Factory method statique
    public static {Name} Create(...)
    {
        // 1. Valider les règles
        // 2. Créer l'instance
        // 3. Raise le DomainEvent de création
        // 4. Retourner l'instance
        throw new NotImplementedException();
    }

    // Méthodes de mutation avec CheckRule + RaiseDomainEvent
}
```

### BusinessRules (une par --rules)
```csharp
public sealed class {RuleName} : BusinessRule
{
    // contexte nécessaire pour évaluer la règle
    public override bool IsBroken() => /* condition */;
    public override string Message => "...";
}
```

### DomainEvents (un par --events)
```csharp
public sealed record {EventName}Event(
    {Name}Id {Name}Id,
    DateTimeOffset OccurredAt) : DomainEvent;
```

### Test skeleton
```csharp
public sealed class {Name}Tests
{
    public sealed class CreateShould
    {
        [Fact] public void ReturnAggregate_WhenValid() { }
        [Fact] public void Raise{FirstEvent}_WhenCreated() { }
        [Fact] public void ThrowBusinessRuleViolation_WhenInvalid() { }
    }
    public sealed class PopDomainEventsShould
    {
        [Fact] public void ReturnAllEvents_ThenClearThem() { }
    }
}
```
