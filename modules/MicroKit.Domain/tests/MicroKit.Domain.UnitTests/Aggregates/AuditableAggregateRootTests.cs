using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

public sealed class AuditableAggregateRootTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Creator = new("user-123");

    [Fact]
    public void Constructor_ExplicitInstant_SetsCreationFields()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        aggregate.CreatedAt.ShouldBe(CreatedAt);
        aggregate.CreatedBy.ShouldBe(Creator);
        aggregate.UpdatedAt.ShouldBeNull();
        aggregate.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Constructor_FakeTimeProvider_DrivesCreatedAt()
    {
        var clock = new FakeTimeProvider(CreatedAt);

        var aggregate = new TestAuditableAggregateRoot(NewId(), clock, Actor.System);

        aggregate.CreatedAt.ShouldBe(CreatedAt);
        aggregate.CreatedBy.ShouldBe(Actor.System);

        clock.Advance(TimeSpan.FromHours(1));
        new TestAuditableAggregateRoot(NewId(), clock, Actor.System)
            .CreatedAt.ShouldBe(CreatedAt.AddHours(1));
    }

    [Fact]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
    {
        var ex = Should.Throw<ArgumentNullException>(
            () => new TestAuditableAggregateRoot(NewId(), (TimeProvider)null!, Creator));

        ex.ParamName.ShouldBe("timeProvider");
    }

    [Fact]
    public void Constructor_NullCreator_ThrowsArgumentNullException()
    {
        var ex = Should.Throw<ArgumentNullException>(
            () => new TestAuditableAggregateRoot(NewId(), CreatedAt, null!));

        ex.ParamName.ShouldBe("createdBy");
    }

    [Fact]
    public void Touch_SetsUpdatedAtAndUpdatedByTogether()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var updatedAt = CreatedAt.AddMinutes(5);
        var updater = new Actor("user-456");

        aggregate.Rename(updatedAt, updater);

        aggregate.UpdatedAt.ShouldBe(updatedAt);
        aggregate.UpdatedBy.ShouldBe(updater);
        aggregate.CreatedAt.ShouldBe(CreatedAt);
        aggregate.CreatedBy.ShouldBe(Creator);
    }

    [Fact]
    public void Touch_NullActor_ThrowsAndLeavesFieldsUnchanged()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        Should.Throw<ArgumentNullException>(() => aggregate.Rename(CreatedAt.AddMinutes(5), null!));

        aggregate.UpdatedAt.ShouldBeNull();
        aggregate.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Constructor_ShouldImplementIAuditableEntity()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        aggregate.ShouldBeAssignableTo<IAuditableEntity>();
        aggregate.ShouldBeAssignableTo<AggregateRoot<TestAuditableId>>();
    }

    [Fact]
    public void Constructor_ShouldSetId()
    {
        var id = NewId();

        new TestAuditableAggregateRoot(id, CreatedAt, Creator).Id.ShouldBe(id);
    }

    [Fact]
    public void RaiseDomainEvent_StoresTheEventAsSupplied()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var domainEvent = new TestAuditableEvent("Test", CreatedAt);

        aggregate.TestRaiseEvent(domainEvent);

        aggregate.DomainEvents.ShouldBe([domainEvent]);
        aggregate.DomainEvents[0].OccurredAt.ShouldBe(CreatedAt);
    }

    [Fact]
    public void UpdateFields_AreSetOnlyThroughTouch()
    {
        System.Reflection.PropertyInfo[] properties =
        [
            typeof(AuditableAggregateRoot<TestAuditableId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableAggregateRoot<TestAuditableId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
        ];

        // A private setter leaves Touch as the only way a derived type can change the field.
        properties.ShouldAllBe(p => p.SetMethod!.IsPrivate);
    }

    private static TestAuditableId NewId() => new(Guid.CreateVersion7());
}

// Test implementations
public sealed record TestAuditableId(Guid Value) : IEntityId;

public sealed record TestAuditableEvent(string Data, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed class TestAuditableAggregateRoot : AuditableAggregateRoot<TestAuditableId>
{
    public TestAuditableAggregateRoot(TestAuditableId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy) { }

    public TestAuditableAggregateRoot(TestAuditableId id, TimeProvider timeProvider, Actor createdBy)
        : base(id, timeProvider, createdBy) { }

    public void TestRaiseEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);

    public void Rename(DateTimeOffset now, Actor actor) => Touch(now, actor);
}
