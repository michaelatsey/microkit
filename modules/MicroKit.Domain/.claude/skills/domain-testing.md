# Skill: Domain Testing

## Quand activer ce skill
- Écriture de tests pour les agrégats, VO, specs, rules
- Tests d'architecture du module Domain
- Debugging d'un test qui échoue sur un invariant

## Philosophie
Le domaine est pur — les tests sont ultra-rapides et sans infrastructure.
Aucun mock nécessaire sauf pour `IDateTimeProvider` si utilisé.

## Builders de test

```csharp
// ✅ Pattern Builder pour les agrégats complexes
public sealed class OrderBuilder
{
    private CustomerId _customerId = CustomerId.New();
    private List<OrderItemRequest> _items = [new(ProductId.New(), 1, Money.From(10, "EUR"))];

    public OrderBuilder WithCustomer(CustomerId id) { _customerId = id; return this; }
    public OrderBuilder WithNoItems() { _items = []; return this; }
    public OrderBuilder WithItems(params OrderItemRequest[] items) { _items = [..items]; return this; }

    public Order Build() => Order.Place(_customerId, _items);
}

// Usage dans les tests
var order = new OrderBuilder().WithNoItems().Build(); // test invariant
var order = new OrderBuilder().Build();               // happy path
```

## Tests d'invariants

```csharp
[Fact]
public void Ship_WhenOrderIsEmpty_ThrowsBusinessRuleViolation()
{
    // Arrange
    var order = new OrderBuilder().WithNoItems().Build();

    // Act
    var act = () => order.Ship(TrackingNumber.From("TRACK-001"));

    // Assert
    act.Should().Throw<BusinessRuleViolationException>()
       .Which.Rule.Should().BeOfType<OrderMustHaveItemsRule>();
}
```

## Tests de DomainEvents

```csharp
[Fact]
public void Place_WithValidItems_RaisesOrderPlacedEvent()
{
    // Arrange
    var customerId = CustomerId.New();
    var items = new[] { new OrderItemRequest(ProductId.New(), 2, Money.From(10, "EUR")) };

    // Act
    var order = Order.Place(customerId, items);
    var events = order.PopDomainEvents();

    // Assert
    events.Should().ContainSingle()
          .Which.Should().BeOfType<OrderPlacedEvent>()
          .Which.CustomerId.Should().Be(customerId);
}

[Fact]
public void PopDomainEvents_ClearsEventsAfterCall()
{
    var order = new OrderBuilder().Build();

    var firstPop = order.PopDomainEvents();
    var secondPop = order.PopDomainEvents();

    firstPop.Should().NotBeEmpty();
    secondPop.Should().BeEmpty();
}
```

## Tests de ValueObjects

```csharp
[Theory]
[InlineData(10, "EUR", 10, "EUR", true)]
[InlineData(10, "EUR", 20, "EUR", false)]
[InlineData(10, "EUR", 10, "USD", false)]
public void Money_Equality(decimal a1, string c1, decimal a2, string c2, bool expected)
{
    var m1 = Money.From(a1, c1);
    var m2 = Money.From(a2, c2);
    m1.Equals(m2).Should().Be(expected);
}

[Fact]
public void Money_Add_WithSameCurrency_ReturnsCorrectSum()
{
    var result = Money.From(10, "EUR").Add(Money.From(5, "EUR"));
    result.Should().Be(Money.From(15, "EUR"));
}
```

## Tests d'architecture (NetArchTest)

```csharp
public sealed class DomainArchitectureTests
{
    private static readonly Assembly DomainAssembly =
        typeof(AggregateRoot<>).Assembly;

    [Fact]
    public void Domain_HasNoDependencyOnOtherMicroKitModules()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("MicroKit.Result", "MicroKit.MediatR", "MicroKit.Persistence")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(result.FailingTypeNames?.FirstOrDefault());
    }

    [Fact]
    public void DomainEvents_ShouldBeSealedRecords()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ImplementInterface(typeof(IDomainEvent))
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void AggregateRoots_ShouldNotHavePublicSetters()
    {
        // Vérifie que les propriétés des agrégats ont private set ou init
        var aggregates = Types.InAssembly(DomainAssembly)
            .That().Inherit(typeof(AggregateRoot<>))
            .GetTypes();

        foreach (var type in aggregates)
        {
            var publicSetters = type.GetProperties()
                .Where(p => p.SetMethod?.IsPublic == true)
                .ToList();

            publicSetters.Should().BeEmpty(
                $"{type.Name} has public setters: {string.Join(", ", publicSetters.Select(p => p.Name))}");
        }
    }
}
```
