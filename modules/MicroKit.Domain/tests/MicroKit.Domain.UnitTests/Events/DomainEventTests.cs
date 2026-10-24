using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Events;

public sealed class DomainEventTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OccurredAt_IsThePositionalArgument()
    {
        new SampleEvent("a", OccurredAt).OccurredAt.ShouldBe(OccurredAt);
    }

    [Fact]
    public void EventId_IsAVersion7Guid()
    {
        new SampleEvent("a", OccurredAt).EventId.Version.ShouldBe(7);
    }

    [Fact]
    public void EventId_IsUniquePerEvent()
    {
        new SampleEvent("a", OccurredAt).EventId.ShouldNotBe(new SampleEvent("a", OccurredAt).EventId);
    }

    [Fact]
    public void EventId_CanBePreservedOnReplay()
    {
        var original = new SampleEvent("a", OccurredAt);

        var replayed = new SampleEvent("a", original.OccurredAt) { EventId = original.EventId };

        replayed.ShouldBe(original);
    }

    [Fact]
    public void OccurredAt_NonZeroOffset_IsStoredAtOffsetZero()
    {
        var plusTwo = OccurredAt.ToOffset(TimeSpan.FromHours(2));

        var evt = new SampleEvent("a", plusTwo);

        evt.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        evt.OccurredAt.UtcDateTime.ShouldBe(plusTwo.UtcDateTime);
    }

    [Fact]
    public void OccurredAt_NonZeroOffsetThroughWith_IsStoredAtOffsetZero()
    {
        var later = OccurredAt.AddMinutes(1).ToOffset(TimeSpan.FromHours(2));

        var evt = new SampleEvent("a", OccurredAt) with { OccurredAt = later };

        evt.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        evt.OccurredAt.UtcDateTime.ShouldBe(later.UtcDateTime);
    }

    [Fact]
    public void OccurredAt_Default_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SampleEvent("a", default))
            .ParamName.ShouldBe("OccurredAt");
    }

    [Fact]
    public void OccurredAt_DefaultThroughWith_ThrowsArgumentOutOfRangeException()
    {
        var evt = new SampleEvent("a", OccurredAt);

        Should.Throw<ArgumentOutOfRangeException>(() => evt with { OccurredAt = default })
            .ParamName.ShouldBe("OccurredAt");
    }

    [Fact]
    public void GuidIdNew_IsAVersion7Guid()
    {
        GuidId.New().Value.Version.ShouldBe(7);
    }

    private sealed record SampleEvent(string Data, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
}
