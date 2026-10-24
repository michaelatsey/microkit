using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

public sealed class AuditableEntityTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Creator = new("user-123");
    private static readonly DateTimeOffset PlusTwo = new(2026, 10, 24, 11, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Constructor_ExplicitInstant_SetsCreationFields()
    {
        var entity = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);

        entity.CreatedAt.ShouldBe(CreatedAt);
        entity.CreatedBy.ShouldBe(Creator);
        entity.UpdatedAt.ShouldBeNull();
        entity.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Constructor_FakeTimeProvider_DrivesCreatedAt()
    {
        var clock = new FakeTimeProvider(CreatedAt);

        var entity = new TestAuditableEntity(GuidId.New(), clock, Actor.Migration);

        entity.CreatedAt.ShouldBe(CreatedAt);
        entity.CreatedBy.ShouldBe(Actor.Migration);
    }

    [Fact]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
    {
        var ex = Should.Throw<ArgumentNullException>(
            () => new TestAuditableEntity(GuidId.New(), (TimeProvider)null!, Creator));

        ex.ParamName.ShouldBe("timeProvider");
    }

    [Fact]
    public void Constructor_NullCreator_ThrowsArgumentNullException()
    {
        var ex = Should.Throw<ArgumentNullException>(
            () => new TestAuditableEntity(GuidId.New(), CreatedAt, null!));

        ex.ParamName.ShouldBe("createdBy");
    }

    [Fact]
    public void Touch_SetsUpdatedAtAndUpdatedByTogether()
    {
        var entity = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);
        var updatedAt = CreatedAt.AddMinutes(5);

        entity.Change(updatedAt, Actor.Anonymous);

        entity.UpdatedAt.ShouldBe(updatedAt);
        entity.UpdatedBy.ShouldBe(Actor.Anonymous);
    }

    [Fact]
    public void Constructor_NonZeroOffset_IsStoredAtOffsetZero()
    {
        var sut = new TestAuditableEntity(GuidId.New(), PlusTwo, Creator);

        sut.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        sut.CreatedAt.UtcDateTime.ShouldBe(PlusTwo.UtcDateTime);
    }

    [Fact]
    public void Constructor_DefaultInstant_ThrowsArgumentOutOfRangeException()
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => new TestAuditableEntity(GuidId.New(), default(DateTimeOffset), Creator));

        ex.ParamName.ShouldBe("createdAt");
    }

    [Fact]
    public void Touch_NonZeroOffset_IsStoredAtOffsetZero()
    {
        var sut = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);
        var later = CreatedAt.AddMinutes(5).ToOffset(TimeSpan.FromHours(2));

        sut.Change(later, Creator);

        sut.UpdatedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        sut.UpdatedAt.Value.UtcDateTime.ShouldBe(later.UtcDateTime);
    }

    [Fact]
    public void Touch_DefaultInstant_ThrowsArgumentOutOfRangeException()
    {
        var sut = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => sut.Change(default, Creator));

        ex.ParamName.ShouldBe("updatedAt");
        sut.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void Touch_BeforeCreatedAt_ThrowsArgumentOutOfRangeException()
    {
        var sut = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);

        var ex = Should.Throw<ArgumentOutOfRangeException>(
            () => sut.Change(CreatedAt.AddTicks(-1), Creator));

        ex.ParamName.ShouldBe("updatedAt");
        sut.UpdatedAt.ShouldBeNull();
        sut.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Touch_EqualToCreatedAt_IsAccepted()
    {
        var sut = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);

        // The same instant in another offset is equal, not earlier.
        sut.Change(CreatedAt.ToOffset(TimeSpan.FromHours(-5)), Creator);

        sut.UpdatedAt.ShouldBe(CreatedAt);
    }

    [Fact]
    public void Touch_NullActor_ThrowsAndLeavesFieldsUnchanged()
    {
        var entity = new TestAuditableEntity(GuidId.New(), CreatedAt, Creator);

        Should.Throw<ArgumentNullException>(() => entity.Change(CreatedAt.AddMinutes(5), null!));

        entity.UpdatedAt.ShouldBeNull();
        entity.UpdatedBy.ShouldBeNull();
    }
}

internal sealed class TestAuditableEntity : AuditableEntity<GuidId>
{
    public TestAuditableEntity(GuidId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy) { }

    public TestAuditableEntity(GuidId id, TimeProvider timeProvider, Actor createdBy)
        : base(id, timeProvider, createdBy) { }

    public void Change(DateTimeOffset now, Actor actor) => Touch(now, actor);
}
