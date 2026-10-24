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
    private static readonly DateTimeOffset PlusTwo = new(2026, 10, 24, 11, 0, 0, TimeSpan.FromHours(2));

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
    public void Mutate_SetsUpdatedAtAndUpdatedByTogether()
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
    public void Mutate_NullActor_ThrowsAndLeavesFieldsUnchanged()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        Should.Throw<ArgumentNullException>(() => aggregate.Rename(CreatedAt.AddMinutes(5), null!));

        aggregate.UpdatedAt.ShouldBeNull();
        aggregate.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Constructor_NonZeroOffset_IsStoredAtOffsetZero()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), PlusTwo, Creator);

        sut.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        sut.CreatedAt.UtcDateTime.ShouldBe(PlusTwo.UtcDateTime);
    }

    [Fact]
    public void Constructor_DefaultInstant_ThrowsArgumentOutOfRangeException()
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => new TestAuditableAggregateRoot(NewId(), default(DateTimeOffset), Creator));

        ex.ParamName.ShouldBe("createdAt");
    }

    [Fact]
    public void Mutate_NonZeroOffset_IsStoredAtOffsetZero()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var later = CreatedAt.AddMinutes(5).ToOffset(TimeSpan.FromHours(2));

        sut.Rename(later, Creator);

        sut.UpdatedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        sut.UpdatedAt.Value.UtcDateTime.ShouldBe(later.UtcDateTime);
    }

    [Fact]
    public void Mutate_DefaultInstant_ThrowsArgumentOutOfRangeException()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => sut.Rename(default, Creator));

        ex.ParamName.ShouldBe("at");
        sut.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void Mutate_BeforeCreatedAt_ThrowsArgumentOutOfRangeException()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        var ex = Should.Throw<ArgumentOutOfRangeException>(
            () => sut.Rename(CreatedAt.AddTicks(-1), Creator));

        ex.ParamName.ShouldBe("at");
        sut.UpdatedAt.ShouldBeNull();
        sut.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Mutate_EarlierThanThePreviousUpdate_ThrowsAndLeavesFieldsUnchanged()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var at1005 = new DateTimeOffset(2026, 10, 24, 10, 5, 0, TimeSpan.Zero);
        var updater = new Actor("user-456");
        sut.Rename(at1005, updater);

        var ex = Should.Throw<ArgumentOutOfRangeException>(
            () => sut.Rename(new DateTimeOffset(2026, 10, 24, 10, 1, 0, TimeSpan.Zero), Creator));

        ex.ParamName.ShouldBe("at");
        ex.Message.ShouldContain("cannot precede 2026-10-24T10:05:00.0000000+00:00, the previous modification");
        sut.UpdatedAt.ShouldBe(at1005);
        sut.UpdatedBy.ShouldBe(updater);
    }

    [Fact]
    public void Mutate_SameInstantTwice_IsAccepted()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var at1005 = new DateTimeOffset(2026, 10, 24, 10, 5, 0, TimeSpan.Zero);

        sut.Rename(at1005, Creator);
        sut.Rename(at1005, Actor.System);

        sut.UpdatedAt.ShouldBe(at1005);
        sut.UpdatedBy.ShouldBe(Actor.System);
    }

    [Fact]
    public void Mutate_EqualToThePreviousUpdateInAnotherOffset_IsAccepted()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var at1005 = new DateTimeOffset(2026, 10, 24, 10, 5, 0, TimeSpan.Zero);
        sut.Rename(at1005, Creator);

        sut.Rename(at1005.ToOffset(TimeSpan.FromHours(2)), Actor.System);

        sut.UpdatedAt.ShouldBe(at1005);
        sut.UpdatedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        sut.UpdatedBy.ShouldBe(Actor.System);
    }

    [Fact]
    public void Mutate_EqualToCreatedAt_IsAccepted()
    {
        var sut = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        // The same instant in another offset is equal, not earlier.
        sut.Rename(CreatedAt.ToOffset(TimeSpan.FromHours(-5)), Creator);

        sut.UpdatedAt.ShouldBe(CreatedAt);
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
    public void UpdateFields_AreSetOnlyThroughMutate()
    {
        System.Reflection.PropertyInfo[] properties =
        [
            typeof(AuditableAggregateRoot<TestAuditableId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableAggregateRoot<TestAuditableId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
        ];

        // A private setter leaves Mutate as the only way a derived type can change the field.
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

    public void Rename(DateTimeOffset now, Actor actor) => Mutate(now, actor, () => { });
}
