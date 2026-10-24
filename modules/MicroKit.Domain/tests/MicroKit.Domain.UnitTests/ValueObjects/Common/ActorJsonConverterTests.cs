using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Events;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.ValueObjects.Common.Json;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

// These tests exercise reflection-based System.Text.Json with default and Web options, which is
// what an application without a source-generated context gets. The test assembly is never trimmed.
#pragma warning disable IL2026
public sealed class ActorJsonConverterTests
{
    public static TheoryData<string> Options => ["default", "web"];

    private static JsonSerializerOptions For(string options) => options switch
    {
        "default" => JsonSerializerOptions.Default,
        "web" => JsonSerializerOptions.Web,
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };

    private static T RoundTrip<T>(T value, string options) => RoundTrip(value, For(options));

    private static T RoundTrip<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_ReservedValue_ThroughTheDefaultConverter_ThrowsJsonExceptionWithAPath(string options)
    {
        var json = options == "web" ? """{"by":"$system"}""" : """{"By":"$system"}""";

        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ActorHolder>(json, For(options)));

        ex.Path.ShouldBe(options == "web" ? "$.by" : "$.By");
        ex.InnerException.ShouldBeOfType<DomainException>();
    }

    // #181, ADR-004 D1: writing a built-in actor carries no risk, so the default converter writes it.
    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltIn_WithTheDefaultConverter_WritesItsValue(string options)
    {
        JsonSerializer.Serialize(Actor.System, For(options)).ShouldBe("\"$system\"");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltInAsDictionaryKey_WithTheDefaultConverter_WritesItsValue(string options)
    {
        var byActor = new Dictionary<Actor, int> { [Actor.System] = 1 };

        JsonSerializer.Serialize(byActor, For(options)).ShouldBe("""{"$system":1}""");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Record_CarryingABuiltInActor_WithTheDefaultConverter_WritesItsValue(string options)
    {
        var json = JsonSerializer.Serialize(new AuditSnapshot(Actor.System), For(options));

        json.ShouldBe(options == "web" ? """{"updatedBy":"$system"}""" : """{"UpdatedBy":"$system"}""");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Record_CarryingABuiltInActor_WrittenWithTheDefaultConverter_CannotBeReadBack(string options)
    {
        var json = JsonSerializer.Serialize(new AuditSnapshot(Actor.System), For(options));

        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<AuditSnapshot>(json, For(options)));

        ex.Path.ShouldBe(options == "web" ? "$.updatedBy" : "$.UpdatedBy");
        ex.InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltInAsDictionaryKey_WrittenWithTheDefaultConverter_CannotBeReadBack(string options)
    {
        var json = JsonSerializer.Serialize(new Dictionary<Actor, int> { [Actor.System] = 1 }, For(options));

        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Actor, int>>(json, For(options)));

        ex.Path.ShouldBe("$.$system");
        ex.InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(Options))]
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
    [MemberData(nameof(Options))]
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

public sealed record ActorHolder(Actor By);

public sealed record AuditSnapshot(Actor UpdatedBy);

public sealed record OrderPlaced(Money Total, string PlacedBy, Actor Customer, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
