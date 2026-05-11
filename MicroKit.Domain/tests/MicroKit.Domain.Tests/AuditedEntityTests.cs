using MicroKit.Domain.Tests.Fakes;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class AuditedEntityTests
{
    [Fact]
    public void NewEntity_CreatedOnUtc_IsApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var entity = new InvoiceEntity(Guid.NewGuid(), 100m);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(entity.CreatedOnUtc, before, after);
    }

    [Fact]
    public void NewEntity_LastModifiedOnUtc_IsNull()
    {
        var entity = new InvoiceEntity(Guid.NewGuid(), 100m);
        Assert.Null(entity.LastModifiedOnUtc);
    }

    [Fact]
    public void NewEntity_CreatedBy_IsNull()
    {
        var entity = new InvoiceEntity(Guid.NewGuid(), 100m);
        Assert.Null(entity.CreatedBy);
    }

    [Fact]
    public void NewEntity_LastModifiedBy_IsNull()
    {
        var entity = new InvoiceEntity(Guid.NewGuid(), 100m);
        Assert.Null(entity.LastModifiedBy);
    }

    [Fact]
    public void AuditFields_HavePrivateSetters()
    {
        var type = typeof(InvoiceEntity);
        foreach (var propName in new[] { "CreatedOnUtc", "CreatedBy", "LastModifiedOnUtc", "LastModifiedBy" })
        {
            var prop = type.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(prop);
            // Must not have a public setter
            Assert.True(prop!.SetMethod is null || !prop.SetMethod.IsPublic,
                $"{propName} must not have a public setter");
        }
    }
}

public sealed class AuditedAggregateRootTests
{
    [Fact]
    public void NewAggregate_LastModifiedOnUtc_IsNull()
    {
        var customer = new CustomerAggregate(Guid.NewGuid(), "user@example.com");
        Assert.Null(customer.LastModifiedOnUtc);
    }

    [Fact]
    public void UpdateTimestamp_SetsLastModifiedOnUtc()
    {
        var customer = new CustomerAggregate(Guid.NewGuid(), "user@example.com");
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        customer.UpdateEmail("new@example.com", "admin");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.NotNull(customer.LastModifiedOnUtc);
        Assert.InRange(customer.LastModifiedOnUtc!.Value, before, after);
    }

    [Fact]
    public void UpdateTimestamp_SetsLastModifiedBy()
    {
        var customer = new CustomerAggregate(Guid.NewGuid(), "user@example.com");
        customer.UpdateEmail("new@example.com", "admin");
        Assert.Equal("admin", customer.LastModifiedBy);
    }

    [Fact]
    public void UpdateTimestamp_NullActor_SetsNullLastModifiedBy()
    {
        var customer = new CustomerAggregate(Guid.NewGuid(), "user@example.com");
        customer.UpdateEmail("new@example.com", null!);
        Assert.Null(customer.LastModifiedBy);
    }

    [Fact]
    public void ImplementsIAuditedEntity()
    {
        var customer = new CustomerAggregate(Guid.NewGuid(), "user@example.com");
        Assert.IsAssignableFrom<MicroKit.Domain.Contracts.IAuditedEntity>(customer);
    }
}
