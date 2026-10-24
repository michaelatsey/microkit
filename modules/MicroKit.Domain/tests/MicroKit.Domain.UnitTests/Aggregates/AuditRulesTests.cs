using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

// AuditRules (#179), the audit rules both auditable bases share (#112, #113, #116, #165, #171, #176),
// run once against each base.
public sealed class AuditRulesTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PlusTwo = new(2026, 10, 24, 11, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset At = CreatedAt.AddMinutes(5);
    private static readonly Actor Creator = new("user-123");
    private static readonly Actor Updater = new("user-456");

    public static TheoryData<string> Bases => [AuditedSubject.Entity, AuditedSubject.Aggregate];

    // --- creation ---

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_ExplicitInstant_SetsCreationFields(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.CreatedAt.ShouldBe(CreatedAt);
        subject.CreatedBy.ShouldBe(Creator);
        subject.UpdatedAt.ShouldBeNull();
        subject.UpdatedBy.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_FakeTimeProvider_DrivesCreatedAt(string kind)
    {
        var clock = new FakeTimeProvider(CreatedAt);

        var subject = AuditedSubject.Create(kind, clock, Actor.System);

        subject.CreatedAt.ShouldBe(CreatedAt);
        subject.CreatedBy.ShouldBe(Actor.System);

        clock.Advance(TimeSpan.FromHours(1));
        AuditedSubject.Create(kind, clock, Actor.System).CreatedAt.ShouldBe(CreatedAt.AddHours(1));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException(string kind)
    {
        Should.Throw<ArgumentNullException>(() => AuditedSubject.Create(kind, (TimeProvider)null!, Creator))
            .ParamName.ShouldBe("timeProvider");
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_NullCreator_ThrowsArgumentNullException(string kind)
    {
        Should.Throw<ArgumentNullException>(() => AuditedSubject.Create(kind, CreatedAt, null!))
            .ParamName.ShouldBe("createdBy");
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_NonZeroOffset_IsStoredAtOffsetZero(string kind)
    {
        var subject = AuditedSubject.Create(kind, PlusTwo, Creator);

        subject.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        subject.CreatedAt.UtcDateTime.ShouldBe(PlusTwo.UtcDateTime);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_DefaultInstant_ThrowsArgumentOutOfRangeException(string kind)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => AuditedSubject.Create(kind, default(DateTimeOffset), Creator))
            .ParamName.ShouldBe("createdAt");
    }

    // --- Mutate: validation before the mutation runs ---

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_InvalidInstant_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var ran = false;

        Should.Throw<ArgumentOutOfRangeException>(
            () => subject.Run(CreatedAt.AddTicks(-1), Updater, () => ran = true));
        Should.Throw<ArgumentOutOfRangeException>(
            () => subject.Run(default, Updater, () => ran = true));

        ran.ShouldBeFalse();
        subject.UpdatedAt.ShouldBeNull();
        subject.UpdatedBy.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_DefaultInstant_ThrowsArgumentOutOfRangeException(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        Should.Throw<ArgumentOutOfRangeException>(() => subject.Run(default, Creator, () => { }))
            .ParamName.ShouldBe("at");
        subject.UpdatedAt.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_BeforeCreatedAt_ThrowsArgumentOutOfRangeException(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => subject.Run(CreatedAt.AddTicks(-1), Creator, () => { }));

        ex.ParamName.ShouldBe("at");
        ex.Message.ShouldContain("cannot precede 2026-10-24T09:00:00.0000000+00:00, the creation instant");
        subject.UpdatedAt.ShouldBeNull();
        subject.UpdatedBy.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_EarlierThanThePreviousUpdate_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var at1005 = new DateTimeOffset(2026, 10, 24, 10, 5, 0, TimeSpan.Zero);
        subject.Run(at1005, Updater, () => { });
        var ran = false;

        var ex = Should.Throw<ArgumentOutOfRangeException>(
            () => subject.Run(new DateTimeOffset(2026, 10, 24, 10, 1, 0, TimeSpan.Zero), Creator, () => ran = true));

        ex.ParamName.ShouldBe("at");
        ex.Message.ShouldContain("cannot precede 2026-10-24T10:05:00.0000000+00:00, the previous modification");
        ran.ShouldBeFalse();
        subject.UpdatedAt.ShouldBe(at1005);
        subject.UpdatedBy.ShouldBe(Updater);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NullActor_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var ran = false;

        Should.Throw<ArgumentNullException>(() => subject.Run(At, null!, () => ran = true))
            .ParamName.ShouldBe("by");

        ran.ShouldBeFalse();
        subject.UpdatedAt.ShouldBeNull();
        subject.UpdatedBy.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NullMutation_ThrowsArgumentNullException(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        Should.Throw<ArgumentNullException>(() => subject.Run(At, Updater, null!))
            .ParamName.ShouldBe("mutation");

        subject.UpdatedAt.ShouldBeNull();
    }

    // --- Mutate: outcome ---

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_SuccessfulMutation_RunsItAndSetsBothFields(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Rename("final", At, Updater);

        subject.Name.ShouldBe("final");
        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Updater);
        subject.CreatedAt.ShouldBe(CreatedAt);
        subject.CreatedBy.ShouldBe(Creator);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_ThrowingMutation_LeavesTheAuditFieldsUnchanged(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Updater, () => { });

        Should.Throw<InvalidOperationException>(
            () => subject.Run(At.AddMinutes(1), Creator, () => throw new InvalidOperationException("rule broken")));

        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Updater);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NonZeroOffset_IsStoredAtOffsetZero(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var later = At.ToOffset(TimeSpan.FromHours(2));

        subject.Run(later, Creator, () => { });

        subject.UpdatedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        subject.UpdatedAt.Value.UtcDateTime.ShouldBe(later.UtcDateTime);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_EqualToCreatedAt_IsAccepted(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        // The same instant in another offset is equal, not earlier.
        subject.Run(CreatedAt.ToOffset(TimeSpan.FromHours(-5)), Creator, () => { });

        subject.UpdatedAt.ShouldBe(CreatedAt);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_SameInstantTwice_IsAccepted(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Run(At, Creator, () => { });
        subject.Run(At, Actor.System, () => { });

        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Actor.System);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_EqualToThePreviousUpdateInAnotherOffset_IsAccepted(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Creator, () => { });

        subject.Run(At.ToOffset(TimeSpan.FromHours(2)), Actor.System, () => { });

        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        subject.UpdatedBy.ShouldBe(Actor.System);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedLaterModification_IsNotOverwrittenByTheOuterOne(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var at1005 = At.AddMinutes(5);

        subject.Run(At, Creator, () => subject.Run(at1005, Updater, () => { }));

        subject.UpdatedAt.ShouldBe(at1005);
        subject.UpdatedBy.ShouldBe(Updater);
        Should.Throw<ArgumentOutOfRangeException>(() => subject.Run(at1005.AddTicks(-1), Creator, () => { }));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_ThrowingMutation_UndoesTheStampOfANestedSuccessfulMutate(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Updater, () => { });

        Should.Throw<InvalidOperationException>(() => subject.Run(At.AddMinutes(1), Creator, () =>
        {
            subject.Run(At.AddMinutes(2), Actor.System, () => { });
            throw new InvalidOperationException("rule broken");
        }));

        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Updater);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedModificationAtTheSameInstant_KeepsTheNestedActor(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Run(At, Creator, () => subject.Run(At, Actor.System, () => { }));

        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Actor.System);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedModificationRepeatingTheCurrentStamp_KeepsTheNestedActor(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Actor.System, () => { });

        // The nested call writes the stamp already there: same instant, same singleton actor.
        subject.Run(At, Creator, () => subject.Run(At, Actor.System, () => { }));

        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Actor.System);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedEarlierModification_IsSupersededByTheOuterOne(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var at1005 = At.AddMinutes(5);

        subject.Run(at1005, Creator, () => subject.Run(At, Updater, () => { }));

        subject.UpdatedAt.ShouldBe(at1005);
        subject.UpdatedBy.ShouldBe(Creator);
    }

    [Fact]
    public void UpdateFields_AreSetOnlyThroughMutate()
    {
        System.Reflection.PropertyInfo[] properties =
        [
            typeof(AuditableAggregateRoot<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableAggregateRoot<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
        ];

        // A private setter leaves Mutate as the only way a derived type can change the field.
        properties.ShouldAllBe(p => p.SetMethod!.IsPrivate);
    }

    // --- aggregate only: domain events ---

    [Fact]
    public void Mutate_ThrowingMutation_RemovesTheEventsItRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("draft", At);
        var kept = aggregate.DomainEvents.ShouldHaveSingleItem();

        Should.Throw<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("lost", At.AddMinutes(1)));
            aggregate.Raise(new Renamed("lost too", At.AddMinutes(1)));
            throw new InvalidOperationException("rule broken");
        }));

        aggregate.DomainEvents.ShouldBe([kept]);
        aggregate.UpdatedAt.ShouldBe(At);
    }

    [Fact]
    public void Mutate_ThrowingMutationThatDrainedEvents_KeepsItsExceptionAndRemovesWhatItRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("one", At);
        aggregate.RenameAndRaise("two", At);
        IReadOnlyList<IDomainEvent> drained = [];

        var ex = Should.Throw<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            drained = aggregate.DrainDomainEvents();
            aggregate.Raise(new Renamed("lost", At.AddMinutes(1)));
            throw new InvalidOperationException("rule broken");
        }));

        ex.Message.ShouldBe("rule broken");
        drained.Count.ShouldBe(2);
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Mutate_ThrowingMutation_KeepsEventsRaisedAfterAnEarlierDrain()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("one", At);
        aggregate.DrainDomainEvents();
        aggregate.RenameAndRaise("two", At);
        var kept = aggregate.DomainEvents.ShouldHaveSingleItem();

        Should.Throw<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("lost", At.AddMinutes(1)));
            throw new InvalidOperationException("rule broken");
        }));

        aggregate.DomainEvents.ShouldBe([kept]);
    }

    [Fact]
    public void Mutate_NestedThrowingMutation_RemovesOnlyTheEventsEachLevelRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("pending", At);
        var pending = aggregate.DomainEvents.ShouldHaveSingleItem();

        Should.Throw<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("outer", At.AddMinutes(1)));
            aggregate.Run(At.AddMinutes(2), Updater, () =>
            {
                aggregate.Raise(new Renamed("inner", At.AddMinutes(2)));
                throw new InvalidOperationException("inner rule broken");
            });
        }));

        aggregate.DomainEvents.ShouldBe([pending]);
    }

    [Fact]
    public void Mutate_InnerRollbackCaughtByTheOuterMutation_KeepsTheOuterEvents()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("pending", At);

        aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("outer", At.AddMinutes(1)));
            Should.Throw<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(2), Updater, () =>
            {
                aggregate.Raise(new Renamed("inner", At.AddMinutes(2)));
                throw new InvalidOperationException("inner rule broken");
            }));
        });

        aggregate.DomainEvents.Select(e => ((Renamed)e).Name).ShouldBe(["pending", "outer"]);
    }

    [Fact]
    public void Mutate_SuccessfulMutation_KeepsTheEventsItRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);

        aggregate.RenameAndRaise("final", At);

        aggregate.DomainEvents.ShouldHaveSingleItem().ShouldBe(new Renamed("final", At) { EventId = aggregate.DomainEvents[0].EventId });
        aggregate.UpdatedAt.ShouldBe(At);
    }
}

internal sealed record Renamed(string Name, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>A uniform view over both auditable bases, so one theory exercises each.</summary>
internal interface IAuditedSubject
{
    string Name { get; }
    DateTimeOffset CreatedAt { get; }
    Actor CreatedBy { get; }
    DateTimeOffset? UpdatedAt { get; }
    Actor? UpdatedBy { get; }

    void Run(DateTimeOffset at, Actor by, Action mutation);

    void Rename(string name, DateTimeOffset at, Actor by);
}

internal static class AuditedSubject
{
    public const string Entity = "AuditableEntity";
    public const string Aggregate = "AuditableAggregateRoot";

    public static IAuditedSubject Create(string kind, DateTimeOffset createdAt, Actor createdBy) => kind switch
    {
        Entity => new AuditedEntity(new GuidId(Guid.CreateVersion7()), createdAt, createdBy),
        Aggregate => new AuditedAggregate(new GuidId(Guid.CreateVersion7()), createdAt, createdBy),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static IAuditedSubject Create(string kind, TimeProvider clock, Actor createdBy) => kind switch
    {
        Entity => new AuditedEntity(new GuidId(Guid.CreateVersion7()), clock, createdBy),
        Aggregate => new AuditedAggregate(new GuidId(Guid.CreateVersion7()), clock, createdBy),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

internal sealed class AuditedEntity : AuditableEntity<GuidId>, IAuditedSubject
{
    public AuditedEntity(GuidId id, DateTimeOffset createdAt, Actor createdBy) : base(id, createdAt, createdBy) { }

    public AuditedEntity(GuidId id, TimeProvider clock, Actor createdBy) : base(id, clock, createdBy) { }

    public string Name { get; private set; } = "draft";

    public void Run(DateTimeOffset at, Actor by, Action mutation) => Mutate(at, by, mutation);

    public void Rename(string name, DateTimeOffset at, Actor by) => Mutate(at, by, () => Name = name);
}

internal sealed class AuditedAggregate : AuditableAggregateRoot<GuidId>, IAuditedSubject
{
    public AuditedAggregate(GuidId id, DateTimeOffset createdAt, Actor createdBy) : base(id, createdAt, createdBy) { }

    public AuditedAggregate(GuidId id, TimeProvider clock, Actor createdBy) : base(id, clock, createdBy) { }

    public string Name { get; private set; } = "draft";

    public void Run(DateTimeOffset at, Actor by, Action mutation) => Mutate(at, by, mutation);

    public void Rename(string name, DateTimeOffset at, Actor by) => Mutate(at, by, () => Name = name);

    public void RenameAndRaise(string name, DateTimeOffset at) => Mutate(at, Actor.System, () =>
    {
        Name = name;
        RaiseDomainEvent(new Renamed(name, at));
    });

    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}
