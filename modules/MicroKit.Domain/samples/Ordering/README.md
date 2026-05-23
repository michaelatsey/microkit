# E-Commerce Ordering Domain Sample

This sample demonstrates a complete ordering domain implementation using MicroKit.Domain primitives. It showcases modern DDD patterns with optimal performance characteristics.

## Domain Model

### Aggregates
- **Order**: Root aggregate managing order lifecycle and items
- **Customer**: Customer information and preferences

### Value Objects
- **Money**: Currency-aware monetary amounts
- **Address**: Shipping and billing addresses
- **Email**: Validated email addresses
- **OrderItem**: Product quantity and pricing

### Domain Events
- **OrderPlacedEvent**: Order creation
- **OrderShippedEvent**: Shipping confirmation
- **OrderCancelledEvent**: Order cancellation

### Business Rules
- **OrderMustHaveItemsRule**: Orders require at least one item
- **OrderCanBeShippedRule**: Status validation for shipping
- **PaymentAmountMustMatchRule**: Payment validation

## Key Patterns Demonstrated

### Modern Value Objects
```csharp
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    public Money
    {
        if (Amount < 0) throw new DomainException("Amount cannot be negative");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
    
    public Money Add(Money other) => 
        Currency == other.Currency 
            ? new(Amount + other.Amount, Currency)
            : throw new DomainException("Currency mismatch");
}
```

### Event-Driven Aggregates
```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    public void Ship(TrackingNumber trackingNumber)
    {
        // 1. Validate business rules
        CheckRule(new OrderCanBeShippedRule(Status));
        
        // 2. Mutate state
        Status = OrderStatus.Shipped;
        ShippedAt = DateTimeOffset.UtcNow;
        
        // 3. Raise domain event AFTER successful mutation
        RaiseDomainEvent(new OrderShippedEvent(Id, trackingNumber, ShippedAt.Value));
    }
}
```

### Strongly-Typed Identifiers
```csharp
public readonly record struct OrderId(Guid Value) : IEntityId
{
    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId Empty => new(Guid.Empty);
}
```

## Usage Examples

### Creating an Order
```csharp
var customerId = CustomerId.New();
var items = new[]
{
    new OrderItemRequest(ProductId.New(), 2, new Money(29.99m, "USD")),
    new OrderItemRequest(ProductId.New(), 1, new Money(15.50m, "USD"))
};

var order = Order.Place(customerId, items);
var events = order.DrainDomainEvents(); // [OrderPlacedEvent]
```

### Processing Payments
```csharp
var paymentAmount = new Money(75.48m, "USD");
order.ProcessPayment(paymentAmount);

var events = order.DrainDomainEvents(); // [OrderPaidEvent]
```

### Shipping Orders
```csharp
var trackingNumber = TrackingNumber.Create("1Z999AA1234567890");
order.Ship(trackingNumber);

var events = order.DrainDomainEvents(); // [OrderShippedEvent]
```

## Performance Characteristics

This sample demonstrates the performance benefits of modern DDD patterns:

- **Zero-allocation equality**: Money and ID comparisons generate no heap pressure
- **Efficient event collection**: Array-based event storage with minimal overhead
- **Optimized business rules**: Direct boolean evaluation without reflection

## Testing Approach

The sample includes comprehensive test coverage:

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
        .Which.Should().BeOfType<OrderShippedEvent>();
}
```

## Integration Notes

This domain model integrates seamlessly with:
- **Entity Framework Core**: Records work perfectly with modern EF
- **System.Text.Json**: Native serialization support
- **MediatR**: Event dispatching through domain event handlers
- **Message queues**: Events serialize directly to JSON

## Running the Sample

```bash
# Build and test
dotnet build samples/Ordering/
dotnet test samples/Ordering/

# Run specific scenarios
dotnet run --project samples/Ordering/ -- create-order
dotnet run --project samples/Ordering/ -- process-payment  
dotnet run --project samples/Ordering/ -- ship-order
```