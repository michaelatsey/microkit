# Implementing Aggregates

## Overview

Aggregates are the core building blocks of domain logic in MicroKit.Domain. They enforce consistency boundaries, coordinate business rules, and manage domain events. This guide demonstrates proven patterns for implementing robust aggregates.

Two rules run through every example:

- **Time and actor are inputs.** An aggregate never reads a clock. The operation receives its instant (`DateTimeOffset now`, or a `TimeProvider` for the auditable bases) and, when it is audited, the `Actor` performing it.
- **One instant per operation.** Every event an operation raises carries that same instant through the positional `OccurredAt` of `DomainEvent`.

## Basic Structure

### Identifiers

```csharp
public readonly record struct CustomerId(Guid Value) : IEntityId
{
    // UUID version 7: time-ordered, so primary-key indexes stay compact
    public static CustomerId New() => new(Guid.CreateVersion7());
}
```

`Entity<TId>` rejects `default(TId)`, so an identifier type exposes no `Empty` value.

### Minimal Aggregate

```csharp
public sealed record CustomerRegisteredEvent(
    CustomerId CustomerId,
    Email Email,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed class Customer : AggregateRoot<CustomerId>
{
    // Private constructor - enforce factory method usage
    private Customer(CustomerId id, Email email, DateTimeOffset registeredAt) : base(id)
    {
        Email = email;
        RegisteredAt = registeredAt;
    }

    public Email Email { get; private set; }
    public DateTimeOffset RegisteredAt { get; private init; }

    // Static factory method with business logic; the instant is an input
    public static Customer Register(Email email, DateTimeOffset now)
    {
        var customer = new Customer(CustomerId.New(), email, now);
        customer.RaiseDomainEvent(new CustomerRegisteredEvent(customer.Id, email, now));

        return customer;
    }
}
```

### Key Patterns

1. **Private Constructor**: Prevents creation without business logic
2. **Static Factory**: Enforces invariants and raises events
3. **Immutable Properties**: Use `private set` or `init`
4. **Event After State**: Raise events after successful mutations
5. **Instant as Input**: The caller passes `now`; the aggregate never reads a clock

### Auditable Aggregate

`AuditableAggregateRoot<TId>` records who created and last changed the aggregate. The creation fields come from the constructor; the update fields change only through `Mutate`, which validates the instant and the actor, runs the change, and sets both fields together only if the change succeeded.

```csharp
public sealed class InvoiceTotalMustBePositiveRule(Money total) : BusinessRule
{
    public override bool IsBroken() => total.Amount <= 0;

    public override string Message => "An invoice total must be positive";

    public override string Code => "invoice.total-must-be-positive";
}

public sealed class Invoice : AuditableAggregateRoot<InvoiceId>
{
    private Invoice(InvoiceId id, Money total, TimeProvider clock, Actor createdBy)
        : base(id, clock, createdBy)
    {
        Total = total;
    }

    public Money Total { get; private set; }

    public static Invoice Issue(Money total, TimeProvider clock, Actor createdBy) =>
        new(InvoiceId.New(), total, clock, createdBy);

    // Mutate validates `now` and `actor` before anything changes, runs the lambda, and sets
    // UpdatedAt and UpdatedBy only if it returns. Check rules first, before any assignment:
    // a lambda that throws halfway keeps the changes it already made.
    public void Adjust(Money total, DateTimeOffset now, Actor actor) =>
        Mutate(now, actor, () =>
        {
            CheckRule(new InvoiceTotalMustBePositiveRule(total));
            Total = total;
        });
}

// A background job has no user in context: it acts as Actor.System
var invoice = Invoice.Issue(
    new Money(120.00m, Currency.FromCode("EUR")), TimeProvider.System, Actor.System);
```

A `Mutate` lambda that captures a parameter, as `Adjust` does, allocates a closure and a delegate on each call. That cost is negligible next to the I/O that persists the change, so `Mutate` has a single overload (ADR-004 D2).

### When a domain method throws

A domain method that throws leaves the aggregate as it was at the moment of the throw:

- **Plain `AggregateRoot<TId>`:** the domain events raised before the throw stay in `DomainEvents`, and the state changed before the throw stays changed. Nothing rolls them back.
- **`AuditableAggregateRoot<TId>`:** `Mutate` removes the domain events raised inside a mutation that throws and restores `UpdatedAt` and `UpdatedBy`, but the field changes made before the throw stay.

So after a domain method throws, the aggregate instance must be discarded, not saved. Saving it, or draining its events, would publish facts of an operation that failed. Load the aggregate again to retry.

```csharp
try
{
    order.Ship(tracking, now);
}
catch (DomainException)
{
    // Do not save `order`: it may hold events and changes of the failed Ship.
    throw;
}
```

Checking every rule before the first assignment and the first `RaiseDomainEvent` keeps a broken rule from leaving anything behind; the discard rule covers every other exception (ADR-004 D3).

## Complex Aggregate Example

```csharp
public sealed record OrderPlacedEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    Money Total,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record OrderItemAddedEvent(
    OrderId OrderId,
    ProductId ProductId,
    int Quantity,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record OrderShippedEvent(
    OrderId OrderId,
    TrackingNumber TrackingNumber,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed class Order : AggregateRoot<OrderId>
{
    private static readonly Currency Eur = Currency.FromCode("EUR");

    private readonly List<OrderItem> _items = [];

    private Order(OrderId id, CustomerId customerId, DateTimeOffset createdAt) : base(id)
    {
        CustomerId = customerId;
        Status = OrderStatus.Draft;
        CreatedAt = createdAt;
    }

    public CustomerId CustomerId { get; private init; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? ShippedAt { get; private set; }

    // Read-only collection exposure
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    // Computed properties
    public Money TotalAmount => _items
        .Select(item => item.TotalPrice)
        .Aggregate(Money.Zero(Eur), (sum, price) => sum.Add(price));

    public static Order Place(
        CustomerId customerId, IReadOnlyList<OrderItemRequest> itemRequests, DateTimeOffset now)
    {
        var order = new Order(OrderId.New(), customerId, now);

        // Business rule validation
        order.CheckRule(new OrderMustHaveItemsRule(itemRequests.Count));

        // Add items with validation; every event of this operation carries `now`
        foreach (var request in itemRequests)
        {
            order.AddItem(request.ProductId, request.Quantity, request.UnitPrice, now);
        }

        // Event after all state mutations complete
        order.RaiseDomainEvent(new OrderPlacedEvent(order.Id, customerId, order.TotalAmount, now));

        return order;
    }

    public void Ship(TrackingNumber trackingNumber, DateTimeOffset now)
    {
        // Pre-conditions
        CheckRule(new OrderCanBeShippedRule(Status));
        CheckRule(new OrderMustHaveItemsRule(_items.Count));

        // State mutation
        Status = OrderStatus.Shipped;
        ShippedAt = now;

        // Event represents completed fact
        RaiseDomainEvent(new OrderShippedEvent(Id, trackingNumber, now));
    }

    private void AddItem(ProductId productId, int quantity, Money unitPrice, DateTimeOffset now)
    {
        CheckRule(new QuantityMustBePositiveRule(quantity));

        var item = OrderItem.Create(productId, quantity, unitPrice);
        _items.Add(item);

        RaiseDomainEvent(new OrderItemAddedEvent(Id, productId, quantity, now));
    }
}
```

## Business Rules Integration

### Implementing Rules

Every rule has a stable `Code`. It is a contract: consumers persist it and map it to API error codes, so it is never derived from the type name.

```csharp
public sealed class OrderMustHaveItemsRule(int itemCount) : BusinessRule
{
    public override bool IsBroken() => itemCount == 0;

    public override string Message =>
        "An order must contain at least one item";

    public override string Code => "order.must-have-items";
}

public sealed class OrderCanBeShippedRule(OrderStatus currentStatus) : BusinessRule
{
    public override bool IsBroken() =>
        currentStatus is not (OrderStatus.Confirmed or OrderStatus.Paid);

    public override string Message =>
        $"Orders in {currentStatus} status cannot be shipped";

    public override string Code => "order.cannot-be-shipped";
}
```

`CheckRule` throws `BusinessRuleViolationException`, which exposes the broken rule's `Code`.

### Using CheckRule Pattern

```csharp
public void ProcessPayment(Money amount, DateTimeOffset now)
{
    // Validate business constraints
    CheckRule(new OrderMustBeInCorrectStatusRule(Status, OrderStatus.Confirmed));
    CheckRule(new PaymentAmountMustMatchOrderTotalRule(amount, TotalAmount));

    // State changes
    Status = OrderStatus.Paid;
    PaidAt = now;

    // Domain event
    RaiseDomainEvent(new OrderPaidEvent(Id, amount, now));
}
```

## Entity Collections

### Managing Child Entities

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];

    // Controlled access through aggregate
    public void AddItem(ProductId productId, int quantity, Money unitPrice, DateTimeOffset now)
    {
        // Validate at aggregate boundary
        CheckRule(new DuplicateItemRule(_items, productId));
        CheckRule(new MaxItemsPerOrderRule(_items.Count));

        var item = OrderItem.Create(productId, quantity, unitPrice);
        _items.Add(item);

        RaiseDomainEvent(new OrderItemAddedEvent(Id, item.ProductId, quantity, now));
    }

    public void RemoveItem(ProductId productId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null) return;

        _items.Remove(item);
        RaiseDomainEvent(new OrderItemRemovedEvent(Id, productId, now));
    }

    // Never expose mutable collections directly
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
}
```

### Child Entity Pattern

```csharp
public sealed class OrderItem : Entity<OrderItemId>
{
    // Private constructor - created only through aggregate
    private OrderItem(OrderItemId id, ProductId productId, int quantity, Money unitPrice)
        : base(id)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public ProductId ProductId { get; private init; }
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private init; }

    // An integer quantity keeps the product within the minor unit; the rounding mode is still
    // stated, because Multiply never chooses one for the caller
    public Money TotalPrice => UnitPrice.Multiply(Quantity, MidpointRounding.ToEven);

    // Factory method with validation
    internal static OrderItem Create(ProductId productId, int quantity, Money unitPrice)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");
        if (unitPrice.Amount <= 0)
            throw new DomainException("Unit price must be positive");

        return new OrderItem(OrderItemId.New(), productId, quantity, unitPrice);
    }

    internal void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw new DomainException("Quantity must be positive");

        Quantity = newQuantity;
    }
}
```

## Event Patterns

### Event Timing

```csharp
public void Cancel(string reason, DateTimeOffset now)
{
    // 1. Validate current state
    if (Status == OrderStatus.Shipped)
        throw new DomainException("Cannot cancel shipped orders");

    // 2. Store previous state for event data
    var previousStatus = Status;

    // 3. Mutate state
    Status = OrderStatus.Cancelled;
    CancelledAt = now;
    CancellationReason = reason;

    // 4. Raise event with both old and new state
    RaiseDomainEvent(new OrderCancelledEvent(Id, previousStatus, reason, now));
}
```

### Event Data Design

```csharp
// Include sufficient data for event handlers; OccurredAt is always the last positional parameter
public sealed record OrderShippedEvent(
    OrderId OrderId,
    CustomerId CustomerId,        // For customer notifications
    TrackingNumber TrackingNumber,
    Money OrderTotal,             // For logistics calculations
    Address ShippingAddress,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
```

`OccurredAt` is stored at offset zero, whatever offset the caller passes. A `with` expression on an event creates a different fact with a new `EventId`; only an explicit `EventId = …` initializer preserves one, for replay or import.

## Performance Considerations

### Efficient Collection Access

```csharp
// ✅ Efficient - lazy evaluation
public Money TotalAmount => _items.Count == 0
    ? Money.Zero(Eur)
    : _items.Select(i => i.TotalPrice).Aggregate((a, b) => a.Add(b));

// ❌ Inefficient - always allocates
public Money TotalAmount => _items.ToList().Aggregate(Money.Zero(Eur), (a, i) => a.Add(i.TotalPrice));
```

### Minimizing Allocations

```csharp
// ✅ Reuse collections where possible
private static readonly IReadOnlyList<OrderItem> EmptyItems = [];

public IReadOnlyList<OrderItem> Items => _items.Count == 0
    ? EmptyItems
    : _items.AsReadOnly();
```

## Testing Patterns

### Aggregate Testing

Tests use Shouldly and pass a fixed instant, so assertions are exact.

```csharp
private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

[Fact]
public void Ship_ValidOrder_RaisesOrderShippedEvent()
{
    // Arrange
    var order = OrderTestBuilder.New()
        .WithStatus(OrderStatus.Paid)
        .WithItems(3)
        .Build();

    var trackingNumber = TrackingNumber.Create("TRK123456");

    // Act
    order.Ship(trackingNumber, Now);

    // Assert
    order.Status.ShouldBe(OrderStatus.Shipped);
    var shipped = order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderShippedEvent>();
    shipped.TrackingNumber.ShouldBe(trackingNumber);
    shipped.OccurredAt.ShouldBe(Now);
}

[Fact]
public void Ship_DraftOrder_ThrowsBusinessRuleViolation()
{
    // Arrange
    var order = OrderTestBuilder.New()
        .WithStatus(OrderStatus.Draft)
        .Build();

    // Act & Assert
    var ex = Should.Throw<BusinessRuleViolationException>(
        () => order.Ship(TrackingNumber.Create("TRK123456"), Now));

    ex.Code.ShouldBe("order.cannot-be-shipped");
}
```

For an auditable aggregate, drive the clock with `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) and assert `CreatedAt` exactly.

### Test Builders

```csharp
public class OrderTestBuilder
{
    private static readonly DateTimeOffset PlacedAt = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    private CustomerId _customerId = CustomerId.New();
    private readonly List<OrderItemRequest> _items = [];
    private OrderStatus? _status;

    public static OrderTestBuilder New() => new();

    public OrderTestBuilder WithCustomer(CustomerId customerId)
    {
        _customerId = customerId;
        return this;
    }

    public OrderTestBuilder WithItems(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _items.Add(new OrderItemRequest(
                ProductId.New(),
                quantity: 1,
                new Money(10m, Currency.FromCode("EUR"))));
        }
        return this;
    }

    public OrderTestBuilder WithStatus(OrderStatus status)
    {
        _status = status;
        return this;
    }

    public Order Build()
    {
        var order = Order.Place(_customerId, _items, PlacedAt);

        if (_status.HasValue && _status != OrderStatus.Draft)
        {
            // Use internal methods to set status for testing
            SetOrderStatus(order, _status.Value);
        }

        // Clear events generated during test setup
        order.DrainDomainEvents();

        return order;
    }
}
```

## Common Pitfalls

### ❌ Exposing Mutable Collections

```csharp
// DON'T: Exposes internal state
public List<OrderItem> Items { get; private set; }

// DO: Provide read-only access
public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
```

### ❌ Events Before State Changes

```csharp
// DON'T: Event represents intention, not fact
RaiseDomainEvent(new OrderWillBeShippedEvent(Id, now));
Status = OrderStatus.Shipped; // Could fail

// DO: Event represents completed action
Status = OrderStatus.Shipped;
RaiseDomainEvent(new OrderShippedEvent(Id, tracking, now));
```

### ❌ Reading a Clock Inside the Aggregate

```csharp
// DON'T: an ambient clock makes tests assert a range and replays impossible
ShippedAt = DateTime.Now;

// DO: the operation receives its instant, and every event it raises carries it
public void Ship(TrackingNumber tracking, DateTimeOffset now) { /* ShippedAt = now; … */ }
```

### ❌ Complex Business Logic in Constructors

```csharp
// DON'T: Constructor validates and mutates
public Order(CustomerId customerId, List<OrderItem> items)
{
    ValidateCustomer(customerId); // External call
    ProcessItems(items);          // Complex logic
}

// DO: Factory method handles complexity
public static Order Place(
    CustomerId customerId, IReadOnlyList<OrderItemRequest> requests, DateTimeOffset now)
{
    var order = new Order(OrderId.New(), customerId, now); // Simple construction
    order.AddItems(requests, now);                         // Business logic
    return order;
}
```

---

This guide provides the foundation for implementing robust, performant aggregates in MicroKit.Domain. Focus on encapsulation, immutability, and clear separation between construction and business logic.
