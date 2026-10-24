using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Domain.Benchmarks;

// Shared benchmark-specific identifier types
public readonly record struct BenchmarkCustomerId(Guid Value) : IEntityId
{
    public static BenchmarkCustomerId New() => new(Guid.NewGuid());
    public static BenchmarkCustomerId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public readonly record struct BenchmarkOrderId(Guid Value) : IEntityId
{
    public static BenchmarkOrderId New() => new(Guid.NewGuid());
    public static BenchmarkOrderId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public readonly record struct BenchmarkProductId(Guid Value) : IEntityId
{
    public static BenchmarkProductId New() => new(Guid.NewGuid());
    public static BenchmarkProductId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

// Currencies resolved once, so measured methods do not include the ISO 4217 lookup
public static class BenchmarkCurrencies
{
    public static readonly Currency Usd = Currency.FromCode("USD");
    public static readonly Currency Eur = Currency.FromCode("EUR");
}

// Supporting value objects for benchmarks
public sealed record TrackingNumber(string Value)
{
    public static TrackingNumber Create(string value) => new(value);
}

public record BenchmarkOrderItemRequest(BenchmarkProductId ProductId, int Quantity, Money UnitPrice);

// Domain Events for benchmarking
public sealed record OrderPlacedEvent(
    BenchmarkOrderId OrderId,
    BenchmarkCustomerId CustomerId,
    Money TotalAmount,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record OrderItemAddedEvent(
    BenchmarkOrderId OrderId,
    BenchmarkProductId ProductId,
    int Quantity,
    Money UnitPrice,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record OrderShippedEvent(
    BenchmarkOrderId OrderId,
    TrackingNumber TrackingNumber,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record CustomerRegisteredEvent(
    BenchmarkCustomerId CustomerId,
    Email EmailAddress,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ProductCreatedEvent(
    BenchmarkProductId ProductId,
    string ProductName,
    Money Price,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record InventoryUpdatedEvent(
    BenchmarkProductId ProductId,
    int PreviousQuantity,
    int NewQuantity,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

// Enums
public enum OrderStatus { Draft, Placed, Shipped, Delivered, Cancelled }