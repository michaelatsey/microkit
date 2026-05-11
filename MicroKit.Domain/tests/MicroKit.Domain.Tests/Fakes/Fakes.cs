using MicroKit.Domain.Abstractions;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Tests.Fakes;

// --- Domain events ---

internal sealed record OrderPlaced(Guid OrderId) : DomainEvent;
internal sealed record OrderCancelled(Guid OrderId) : DomainEvent;

// --- Entity ---

internal sealed class ProductEntity : Entity<Guid>
{
    public string Name { get; }

    public ProductEntity(Guid id, string name) : base(id)
    {
        Name = name;
    }

    // EF Core parameterless ctor
    private ProductEntity() { Name = string.Empty; }
}

// --- AggregateRoot ---

internal sealed class OrderAggregate : AggregateRoot<Guid>
{
    public string Reference { get; private set; } = string.Empty;

    public OrderAggregate(Guid id, string reference) : base(id)
    {
        Reference = reference;
        AddDomainEvent(new OrderPlaced(id));
        IncrementVersion();
    }

    public void Cancel()
    {
        AddDomainEvent(new OrderCancelled(Id));
        IncrementVersion();
    }

    public void RemoveEvent(IDomainEvent e) => RemoveDomainEvent(e);
}

// --- ValueObject ---

internal sealed class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }

    public Address(string street, string city)
    {
        Street = street;
        City = city;
    }

    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return Street;
        yield return City;
    }
}

// --- AuditedEntity ---

internal sealed class InvoiceEntity : AuditedEntity<Guid>
{
    public decimal Total { get; }

    public InvoiceEntity(Guid id, decimal total) : base(id)
    {
        Total = total;
    }
}

// --- AuditedAggregateRoot ---

internal sealed class CustomerAggregate : AuditedAggregateRoot<Guid>
{
    public string Email { get; private set; } = string.Empty;

    public CustomerAggregate(Guid id, string email) : base(id)
    {
        Email = email;
    }

    public void UpdateEmail(string email, string modifiedBy)
    {
        Email = email;
        UpdateTimestamp(modifiedBy);
    }
}

// --- Enumeration ---

internal sealed class OrderStatus : Enumeration
{
    public static readonly OrderStatus Pending = new(1, "Pending", "Pending");
    public static readonly OrderStatus Completed = new(2, "Completed", "Completed");
    public static readonly OrderStatus Cancelled = new(3, "Cancelled", "Cancelled");

    private OrderStatus(int id, string name, string displayName)
        : base(id, name, displayName) { }
}
