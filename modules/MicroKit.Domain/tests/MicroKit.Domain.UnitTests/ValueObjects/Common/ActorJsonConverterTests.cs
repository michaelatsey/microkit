using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Events;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.ValueObjects.Common.Json;
using Shouldly;
using Xunit;
using static MicroKit.Domain.UnitTests.ValueObjects.Common.JsonTestOptions;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

// These tests exercise reflection-based System.Text.Json with default and Web options, which is
// what an application without a source-generated context gets. The test assembly is never trimmed.
#pragma warning disable IL2026
public sealed class ActorJsonConverterTests
{
    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_ReservedValue_ThroughTheDefaultConverter_ThrowsJsonExceptionWithAPath(string options)
    {
        var json = options == "web" ? """{"by":"$system"}""" : """{"By":"$system"}""";

        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ActorHolder>(json, For(options)));

        ex.Path.ShouldBe(options == "web" ? "$.by" : "$.By");
        ex.InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_BuiltIn_WithTheDefaultConverter_FailsOnWrite(string options)
    {
        var ex = Should.Throw<JsonException>(() => JsonSerializer.Serialize(Actor.System, For(options)));

        ex.Message.ShouldContain(nameof(TrustedActorJsonConverter));
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_BuiltInAsDictionaryKey_WithTheDefaultConverter_FailsOnWrite(string options)
    {
        var byActor = new Dictionary<Actor, int> { [Actor.System] = 1 };

        Should.Throw<JsonException>(() => JsonSerializer.Serialize(byActor, For(options)))
            .Message.ShouldContain(nameof(TrustedActorJsonConverter));
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_User_RoundTrips(string options)
    {
        var actor = new Actor("user-123");

        JsonSerializer.Serialize(actor, For(options)).ShouldBe("\"user-123\"");
        RoundTrip(actor, options).ShouldBe(actor);
    }

    [Fact]
    public void Actor_UnknownReservedValue_FailsOnDeserialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Actor>("\"$root\""))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void DomainEvent_CarryingMoneyAUserActorAndABuiltInActorValue_RoundTrips(string options)
    {
        // A built-in actor travels as its value until the outbox can register the trusted converter (#174).
        var placed = new OrderPlaced(
            new Money(99.99m, Currency.FromCode("EUR")),
            Actor.System.Value,
            new Actor("user-123"),
            new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        var read = RoundTrip(placed, options);

        read.Total.ShouldBe(placed.Total);
        Actor.Restore(read.PlacedBy).ShouldBeSameAs(Actor.System);
        read.Customer.ShouldBe(placed.Customer);
        read.OccurredAt.ShouldBe(placed.OccurredAt);
        read.EventId.ShouldBe(placed.EventId);
        read.ShouldBe(placed);
    }
}
#pragma warning restore IL2026
