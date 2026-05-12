# MicroKit.Domain

Domain primitives for .NET 10. Entity, AggregateRoot, ValueObject, Result, Error, Money, and Enumeration — all zero third-party dependencies.

---

## What makes this production-grade

**Identity guards at construction time.** `Entity<TKey>` uses `EqualityComparer<TKey>.Default.Equals(id, default!)` to reject `Guid.Empty`, `0`, and null before the object is created. There is no separate validation step, no convention, and no chance of a default-keyed entity being persisted.

**Dual optimistic concurrency on aggregates.** `AggregateRootBase` carries two independent concurrency tokens. `Version` is an int incremented by domain methods via `IncrementVersion()` — it tracks logical mutations and is meaningful at the domain layer. `RowVersion` is a `byte[]` set by the EF Core persistence layer — it is the database-level CAS token. Both exist because they address different failure modes: `Version` detects in-memory mutation order; `RowVersion` enforces database-level last-writer-wins.

**Domain event encapsulation.** The event list is a private `List<IDomainEvent>` exposed only as `IReadOnlyCollection<IDomainEvent>`. Events are added and removed exclusively through `protected` aggregate methods. The persistence layer calls `ClearDomainEvents()` after dispatch. External code cannot mutate the event queue.

**Result invariants enforced at construction.** `Result` and `Result<T>` throw `InvalidOperationException` in the constructor if `isSuccess && error != Error.None` or `!isSuccess && error == Error.None`. These are illegal states; the library makes them impossible to construct accidentally. `Error.None` is a sentinel value — there is no null-checking on the error field.

**ISO 4217 currency precision without a lookup table.** `Money.DecimalDigits` reads the decimal digit count dynamically from the .NET culture registry via `NumberFormatInfo.CurrencyDecimalDigits`. The culture scan happens once at class initialization via a static `IReadOnlyDictionary<string, NumberFormatInfo>`. JPY returns 0 digits; EUR and USD return 2; KWD returns 3. No hardcoded switch statement.

**Accounting rounding on every monetary operation.** `Money.RoundedAmount` uses `MidpointRounding.AwayFromZero` — the accounting standard — not banker's rounding (which .NET defaults to). `ToSmallestUnit()` applies the same rounding before converting to cents/yen/fils for payment processor integration. Cross-currency arithmetic throws a typed `CurrencyMismatchException` before any calculation occurs.

**Rich Enumeration replaces fragile C# enums.** `Enumeration` provides `Id`, `Name`, `DisplayName`, and `Description`. Lookup is available by id (`FromId<T>`), name (`FromName<T>` — case-insensitive), or display name (`FromDisplayName<T>`). The lookup throws `InvalidOperationException` with a precise message; it never returns null.

**Audited base classes with interceptor-only setters.** `AuditedEntity` exposes `CreatedOnUtc`, `CreatedBy`, `LastModifiedOnUtc`, and `LastModifiedBy` with `private set`. The `SetAuditFields(...)` method is `internal` — only EF Core interceptors in the same assembly can call it. Application code cannot accidentally overwrite audit timestamps.

---

## Installation

```shell
# Contracts — IEntity, IDomainEvent, IAggregateRoot — zero deps
dotnet add package MicroKit.Domain.Contracts

# Base classes — Entity<TKey>, AggregateRoot<TKey>, ValueObject, DomainEvent, Enumeration, AuditedAggregateRoot<TKey>
dotnet add package MicroKit.Domain.Abstractions

# Result<T>, Error, Money, typed domain exceptions
dotnet add package MicroKit.Domain
```

---

## Usage

### Entity with identity guard

```csharp
using MicroKit.Domain.Abstractions;

public sealed class Product : Entity<Guid>
{
    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }

    private Product() { }

    public Product(Guid id, string name, decimal price) : base(id)
    {
        // base(id) throws ArgumentException if id == Guid.Empty
        Name = name;
        Price = price;
    }
}
```

### AggregateRoot — domain events and optimistic concurrency

```csharp
using MicroKit.Domain.Abstractions;

public sealed record OrderPlacedEvent(Guid OrderId, string CustomerId) : DomainEvent;
public sealed record OrderCancelledEvent(Guid OrderId) : DomainEvent;

public sealed class Order : AggregateRoot<Guid>
{
    public string CustomerId { get; private set; } = null!;
    public OrderStatus Status { get; private set; }

    private Order() { }

    public static Order Place(Guid id, string customerId)
    {
        var order = new Order { Status = OrderStatus.Pending };
        order.AddDomainEvent(new OrderPlacedEvent(id, customerId));
        order.IncrementVersion();   // Version becomes 1
        return order;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be cancelled.");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new OrderCancelledEvent(Id));
        IncrementVersion();         // Version becomes 2
    }
}

// After SaveChanges, the persistence layer calls:
// order.ClearDomainEvents();
```

`AggregateRootBase` members:

| Member | Purpose |
|---|---|
| `Version` | Int incremented by `IncrementVersion()` inside domain methods |
| `RowVersion` | `byte[]` set by EF Core; used as database concurrency token |
| `DomainEvents` | `IReadOnlyCollection<IDomainEvent>` — externally read-only |
| `AddDomainEvent(IDomainEvent)` | `protected` — only callable from within the aggregate |
| `RemoveDomainEvent(IDomainEvent)` | `protected` — for intra-aggregate corrections |
| `ClearDomainEvents()` | `public` — called by the persistence layer after dispatch |
| `IncrementVersion()` | `protected` — call once per state-changing domain method |

### Audited aggregate

```csharp
public sealed class Invoice : AuditedAggregateRoot<Guid>
{
    public decimal Total { get; private set; }

    private Invoice() { }

    public Invoice(Guid id, decimal total) : base(id)
    {
        Total = total;
    }

    public void Adjust(decimal newTotal, string adjustedBy)
    {
        Total = newTotal;
        UpdateTimestamp(adjustedBy);   // sets LastModifiedOnUtc and LastModifiedBy
        IncrementVersion();
    }

    // CreatedOnUtc, CreatedBy are set by the EF Core interceptor at first save.
    // SetAuditFields() is internal — application code cannot call it.
}
```

### ValueObject

```csharp
using MicroKit.Domain.Abstractions;

public sealed class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string PostalCode { get; }

    public Address(string street, string city, string postalCode)
    {
        Street = street;
        City = city;
        PostalCode = postalCode;
    }

    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return Street;
        yield return City;
        yield return PostalCode;
    }
}

// Equality is structural — no identity involved
var a = new Address("1 Main St", "Paris", "75001");
var b = new Address("1 Main St", "Paris", "75001");
Console.WriteLine(a == b);   // true
Console.WriteLine(a.Equals(b)); // true
```

### Result and Error

```csharp
using MicroKit.Domain.Primitives;

public static class OrderErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Order.NotFound", "The requested order does not exist.");

    public static readonly Error AlreadyCancelled =
        Error.Conflict("Order.AlreadyCancelled", "The order has already been cancelled.");
}

// Return Result<T> — no exceptions for expected business outcomes
public async Task<Result<OrderDto>> FindAsync(Guid id, CancellationToken ct)
{
    var order = await _orders.FindByIdAsync(id, ct);
    return order is null
        ? Result.Failure<OrderDto>(OrderErrors.NotFound)
        : Result.Success(new OrderDto(order));
}

// Implicit conversion: Error → Result<T>
public Result<Order> Get(Guid id) =>
    _cache.TryGet(id, out var o) ? o : OrderErrors.NotFound;

// Call site
var result = await FindAsync(id, ct);
if (result.IsFailure)
    return Problem(result.Error.Message, statusCode: 404);

var dto = result.Value; // safe — IsSuccess guarantees Value is non-null
```

`Error` factory methods:

| Method | `ErrorType` |
|---|---|
| `Error.Failure(code, message)` | `Failure` |
| `Error.NotFound(code, message)` | `NotFound` |
| `Error.Conflict(code, message)` | `Conflict` |
| `Error.Validation(code, message)` | `Validation` |
| `Error.Unauthorized(code, message)` | `Unauthorized` |
| `Error.Forbidden(code, message)` | `Forbidden` |

### Money

```csharp
using MicroKit.Domain.ValueObjects;

// ISO 4217 validation — 3-letter alphabetic code only
var price = new Money(99.99m, "EUR");    // ok
var jpy   = new Money(1500m, "JPY");     // ok; DecimalDigits == 0
// new Money(10m, "XX") throws ArgumentException — not a valid ISO code

// Arithmetic — cross-currency throws CurrencyMismatchException
var tax       = price.CalculateTax(0.20m);   // Money(19.998, EUR)
var total     = price.AddTax(0.20m);          // Money(119.988, EUR)
var rounded   = total.RoundedAmount;          // 119.99 — AwayFromZero

// Payment processor integration
long cents = new Money(9.995m, "USD").ToSmallestUnit(); // 1000 (rounded away from zero)
var back   = Money.FromSmallestUnit(1000L, "USD");       // Money(10.00, USD)

// Proration for billing cycles
var monthly = new Money(30m, "USD");
var prorated = monthly.CalculateProration(
    fromDate:             DateTimeOffset.UtcNow.AddDays(-10),
    toDate:               DateTimeOffset.UtcNow,
    billingPeriodStart:   DateTimeOffset.UtcNow.AddDays(-30),
    billingPeriodEnd:     DateTimeOffset.UtcNow);
// prorated.Amount ≈ 10.00

// Collection aggregation — currency-homogeneous
var total2 = Money.Sum([new Money(10m, "EUR"), new Money(5m, "EUR")]); // 15 EUR
```

### Enumeration

```csharp
using MicroKit.Domain.Abstractions;

public sealed class OrderStatus : Enumeration
{
    public static readonly OrderStatus Pending   = new(1, nameof(Pending),   "Pending payment");
    public static readonly OrderStatus Confirmed = new(2, nameof(Confirmed), "Payment confirmed");
    public static readonly OrderStatus Shipped   = new(3, nameof(Shipped),   "Shipped to customer");

    private OrderStatus(int id, string name, string? displayName = null)
        : base(id, name, displayName) { }
}

// Type-safe lookup — throws InvalidOperationException on no match, never returns null
var status = Enumeration.FromId<OrderStatus>(2);        // Confirmed
var same   = Enumeration.FromName<OrderStatus>("shipped"); // Shipped (case-insensitive)
var all    = Enumeration.GetAll<OrderStatus>();          // all three instances
```

---

## Configuration

No DI registration is required for any package in this module. These are pure types — base classes, records, and sealed classes. Plug them into your domain layer and reference them from any package tier.

---

## Package dependency graph

```
MicroKit.Domain.Contracts
    (no NuGet dependencies)

MicroKit.Domain.Abstractions
    MicroKit.Domain.Contracts

MicroKit.Domain
    MicroKit.Domain.Abstractions
    MicroKit.Domain.Contracts
```
