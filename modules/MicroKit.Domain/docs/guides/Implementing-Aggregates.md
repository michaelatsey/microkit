# Implementing Aggregates

## Overview

Aggregates are the core building blocks of domain logic in MicroKit.Domain. They enforce consistency boundaries, coordinate business rules, and manage domain events. This guide demonstrates proven patterns for implementing robust aggregates.

## Basic Structure

### Minimal Aggregate

```csharp
public sealed class Customer : AggregateRoot<CustomerId>
{
    // Private constructor - enforce factory method usage
    private Customer(CustomerId id, Email email) : base(id)
    {
        Email = email;
        RegistrationDate = DateTimeOffset.UtcNow;
    }

    public Email Email { get; private set; }
    public DateTimeOffset RegistrationDate { get; private init; }

    // Static factory method with business logic
    public static Customer Register(Email email)
    {
        var customer = new Customer(CustomerId.New(), email);
        customer.RaiseDomainEvent(new CustomerRegisteredEvent(
            customer.Id, 
            email, 
            customer.RegistrationDate));
        
        return customer;
    }
}
```

### Key Patterns

1. **Private Constructor**: Prevents creation without business logic
2. **Static Factory**: Enforces invariants and raises events
3. **Immutable Properties**: Use `private set` or `init`
4. **Event After State**: Raise events after successful mutations

## Complex Aggregate Example

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];
    
    private Order(OrderId id, CustomerId customerId) : base(id)
    {
        CustomerId = customerId;
        Status = OrderStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
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
        .Aggregate(Money.Zero("USD"), (sum, price) => sum.Add(price));

    public static Order Place(CustomerId customerId, IReadOnlyList<OrderItemRequest> itemRequests)
    {
        var order = new Order(OrderId.New(), customerId);
        
        // Business rule validation
        order.CheckRule(new OrderMustHaveItemsRule(itemRequests));
        order.CheckRule(new CustomerMustExistRule(customerId));
        
        // Add items with validation
        foreach (var request in itemRequests)
        {
            order.AddItem(request.ProductId, request.Quantity, request.UnitPrice);
        }
        
        // Event after all state mutations complete
        order.RaiseDomainEvent(new OrderPlacedEvent(
            order.Id, 
            customerId, 
            order.TotalAmount,
            order.CreatedAt));
        
        return order;
    }

    public void Ship(TrackingNumber trackingNumber)
    {
        // Pre-conditions
        CheckRule(new OrderCanBeShippedRule(Status));
        CheckRule(new OrderMustHaveItemsRule(_items));
        
        // State mutation
        Status = OrderStatus.Shipped;
        ShippedAt = DateTimeOffset.UtcNow;
        
        // Event represents completed fact
        RaiseDomainEvent(new OrderShippedEvent(
            Id, 
            trackingNumber, 
            ShippedAt.Value));
    }

    private void AddItem(ProductId productId, int quantity, Money unitPrice)
    {
        CheckRule(new QuantityMustBePositiveRule(quantity));
        CheckRule(new UnitPriceMustBePositiveRule(unitPrice));
        
        var item = new OrderItem(productId, quantity, unitPrice);
        _items.Add(item);
        
        RaiseDomainEvent(new OrderItemAddedEvent(Id, productId, quantity));
    }
}
```

## Business Rules Integration

### Implementing Rules

```csharp
public sealed class OrderMustHaveItemsRule(IEnumerable<OrderItem> items) : BusinessRule
{
    public override bool IsBroken() => !items.Any();
    
    public override string Message => 
        "An order must contain at least one item";
}

public sealed class OrderCanBeShippedRule(OrderStatus currentStatus) : BusinessRule
{
    public override bool IsBroken() => 
        currentStatus is not (OrderStatus.Confirmed or OrderStatus.Paid);
    
    public override string Message => 
        $"Orders in {currentStatus} status cannot be shipped";
}
```

### Using CheckRule Pattern

```csharp
public void ProcessPayment(Money amount)
{
    // Validate business constraints
    CheckRule(new OrderMustBeInCorrectStatusRule(Status, OrderStatus.Confirmed));
    CheckRule(new PaymentAmountMustMatchOrderTotalRule(amount, TotalAmount));
    
    // State changes
    Status = OrderStatus.Paid;
    PaidAt = DateTimeOffset.UtcNow;
    
    // Domain event
    RaiseDomainEvent(new OrderPaidEvent(Id, amount, PaidAt.Value));
}
```

## Entity Collections

### Managing Child Entities

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];
    
    // Controlled access through aggregate
    public void AddItem(ProductId productId, int quantity, Money unitPrice)
    {
        // Validate at aggregate boundary
        CheckRule(new DuplicateItemRule(_items, productId));
        CheckRule(new MaxItemsPerOrderRule(_items.Count));
        
        var item = OrderItem.Create(productId, quantity, unitPrice);
        _items.Add(item);
        
        RaiseDomainEvent(new OrderItemAddedEvent(Id, item.ProductId, quantity));
    }
    
    public void RemoveItem(ProductId productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null) return;
        
        _items.Remove(item);
        RaiseDomainEvent(new OrderItemRemovedEvent(Id, productId));
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
    public Money TotalPrice => UnitPrice.Multiply(Quantity);
    
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
public void Cancel(string reason)
{
    // 1. Validate current state
    if (Status == OrderStatus.Shipped)
        throw new DomainException("Cannot cancel shipped orders");
    
    // 2. Store previous state for event data
    var previousStatus = Status;
    
    // 3. Mutate state
    Status = OrderStatus.Cancelled;
    CancelledAt = DateTimeOffset.UtcNow;
    CancellationReason = reason;
    
    // 4. Raise event with both old and new state
    RaiseDomainEvent(new OrderCancelledEvent(
        Id, 
        previousStatus, 
        reason, 
        CancelledAt.Value));
}
```

### Event Data Design

```csharp
// Include sufficient data for event handlers
public sealed record OrderShippedEvent(
    OrderId OrderId,
    CustomerId CustomerId,        // For customer notifications
    TrackingNumber TrackingNumber,
    Money OrderTotal,             // For logistics calculations
    DateTimeOffset ShippedAt,
    Address ShippingAddress) : DomainEvent;
```

## Performance Considerations

### Efficient Collection Access

```csharp
// ✅ Efficient - lazy evaluation
public Money TotalAmount => _items.Count == 0 
    ? Money.Zero("USD")
    : _items.Select(i => i.TotalPrice).Aggregate((a, b) => a.Add(b));

// ❌ Inefficient - always allocates
public Money TotalAmount => _items.ToList().Sum(i => i.TotalPrice);
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

```csharp
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
    order.Ship(trackingNumber);

    // Assert
    order.Status.Should().Be(OrderStatus.Shipped);
    order.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<OrderShippedEvent>()
        .Which.TrackingNumber.Should().Be(trackingNumber);
}

[Fact]
public void Ship_DraftOrder_ThrowsBusinessRuleViolation()
{
    // Arrange
    var order = OrderTestBuilder.New()
        .WithStatus(OrderStatus.Draft)
        .Build();

    // Act & Assert
    var act = () => order.Ship(TrackingNumber.Create("TRK123456"));
    
    act.Should().Throw<BusinessRuleViolationException>()
        .WithMessage("*cannot be shipped*");
}
```

### Test Builders

```csharp
public class OrderTestBuilder
{
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
                new Money(10m, "USD")));
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
        var order = Order.Place(_customerId, _items);
        
        if (_status.HasValue && _status != OrderStatus.Draft)
        {
            // Use reflection or internal methods to set status for testing
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
RaiseDomainEvent(new OrderWillBeShippedEvent(Id));
Status = OrderStatus.Shipped; // Could fail

// DO: Event represents completed action
Status = OrderStatus.Shipped;
RaiseDomainEvent(new OrderShippedEvent(Id));
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
public static Order Place(CustomerId customerId, IReadOnlyList<OrderItemRequest> requests)
{
    var order = new Order(OrderId.New(), customerId); // Simple construction
    order.AddItems(requests);                          // Business logic
    return order;
}
```

---

This guide provides the foundation for implementing robust, performant aggregates in MicroKit.Domain. Focus on encapsulation, immutability, and clear separation between construction and business logic.