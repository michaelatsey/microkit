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
public sealed class TrustedActorJsonConverterTests
{
    public static TheoryData<string> Options => ["default", "web"];

    private static JsonSerializerOptions For(string options) => options switch
    {
        "default" => JsonSerializerOptions.Default,
        "web" => JsonSerializerOptions.Web,
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };

    private static JsonSerializerOptions Trusted(string options) =>
        new(For(options)) { Converters = { new TrustedActorJsonConverter() } };

    private static T RoundTrip<T>(T value, string options) => RoundTrip(value, For(options));

    private static T RoundTrip<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltIn_WithTheTrustedConverter_RoundTripsToTheSameInstance(string options)
    {
        JsonSerializer.Serialize(Actor.System, Trusted(options)).ShouldBe("\"$system\"");
        RoundTrip(Actor.System, Trusted(options)).ShouldBeSameAs(Actor.System);
        RoundTrip(Actor.Migration, Trusted(options)).ShouldBeSameAs(Actor.Migration);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltInAsDictionaryKey_WithTheTrustedConverter_RoundTripsToTheSameInstance(string options)
    {
        var byActor = new Dictionary<Actor, int> { [Actor.System] = 1 };

        var read = RoundTrip(byActor, Trusted(options));

        read.Keys.Single().ShouldBeSameAs(Actor.System);
        read[Actor.System].ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Record_CarryingABuiltInActor_WithTheTrustedConverter_RoundTripsToTheSameInstance(string options)
    {
        RoundTrip(new StoredAuditSnapshot(Actor.System), Trusted(options)).UpdatedBy.ShouldBeSameAs(Actor.System);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_User_WithTheTrustedConverter_RoundTrips(string options)
    {
        var actor = new Actor("user-123");

        RoundTrip(actor, Trusted(options)).ShouldBe(actor);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_UnknownReservedValue_WithTheTrustedConverter_FailsOnDeserialization(string options)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Actor>("\"$root\"", Trusted(options)))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void DomainEvent_CarryingABuiltInActor_RoundTripsOnlyWithTheTrustedConverter(string options)
    {
        var archived = new OrderArchived(Actor.System, new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        // Written with default options, but readable only with the trusted converter (ADR-004 D1).
        var json = JsonSerializer.Serialize(archived, For(options));
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<OrderArchived>(json, For(options)));

        var read = RoundTrip(archived, Trusted(options));
        read.ArchivedBy.ShouldBeSameAs(Actor.System);
        read.ShouldBe(archived);
    }
}
#pragma warning restore IL2026

public sealed record OrderArchived(Actor ArchivedBy, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record StoredAuditSnapshot(Actor UpdatedBy);
