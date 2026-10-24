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
public sealed class TrustedActorJsonConverterTests
{
    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_BuiltIn_WithTheTrustedConverter_RoundTripsToTheSameInstance(string options)
    {
        JsonSerializer.Serialize(Actor.System, Trusted(options)).ShouldBe("\"$system\"");
        RoundTrip(Actor.System, Trusted(options)).ShouldBeSameAs(Actor.System);
        RoundTrip(Actor.Migration, Trusted(options)).ShouldBeSameAs(Actor.Migration);
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_User_WithTheTrustedConverter_RoundTrips(string options)
    {
        var actor = new Actor("user-123");

        RoundTrip(actor, Trusted(options)).ShouldBe(actor);
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Actor_UnknownReservedValue_WithTheTrustedConverter_FailsOnDeserialization(string options)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Actor>("\"$root\"", Trusted(options)))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void DomainEvent_CarryingABuiltInActor_RoundTripsOnlyWithTheTrustedConverter(string options)
    {
        var archived = new OrderArchived(Actor.System, new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        // Refused when written, inside the business transaction, not when read back later.
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(archived, For(options)))
            .Message.ShouldContain(nameof(TrustedActorJsonConverter));

        var read = RoundTrip(archived, Trusted(options));
        read.ArchivedBy.ShouldBeSameAs(Actor.System);
        read.ShouldBe(archived);
    }
}
#pragma warning restore IL2026
