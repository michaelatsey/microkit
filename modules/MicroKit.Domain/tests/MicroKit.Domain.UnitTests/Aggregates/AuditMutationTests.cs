using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

// Mutate (#176), run against both auditable bases.
public sealed class AuditMutationTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset At = CreatedAt.AddMinutes(5);
    private static readonly Actor Creator = new("user-123");
    private static readonly Actor Updater = new("user-456");

    public static TheoryData<string> Bases => [AuditedSubject.Entity, AuditedSubject.Aggregate];

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
    public void Mutate_InstantBeforeThePreviousUpdate_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Updater, () => { });
        var ran = false;

        Should.Throw<ArgumentOutOfRangeException>(
            () => subject.Run(At.AddTicks(-1), Creator, () => ran = true));

        ran.ShouldBeFalse();
        subject.UpdatedAt.ShouldBe(At);
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
    public void Mutate_SuccessfulMutation_RunsItAndSetsBothFields(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Rename("final", At, Updater);

        subject.Name.ShouldBe("final");
        subject.UpdatedAt.ShouldBe(At);
        subject.UpdatedBy.ShouldBe(Updater);
    }

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
