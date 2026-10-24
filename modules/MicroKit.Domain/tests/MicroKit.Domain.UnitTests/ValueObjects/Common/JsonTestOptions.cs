using System.Text.Json;
using MicroKit.Domain.Events;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.ValueObjects.Common.Json;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

// Shared by the JSON converter tests: the two option sets each test runs with, and its payloads.
// The test assembly is never trimmed, so reflection-based System.Text.Json is fine here.
#pragma warning disable IL2026
internal static class JsonTestOptions
{
    public static TheoryData<string> Options => ["default", "web"];

    public static JsonSerializerOptions For(string options) => options switch
    {
        "default" => JsonSerializerOptions.Default,
        "web" => JsonSerializerOptions.Web,
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };

    public static JsonSerializerOptions Trusted(string options) =>
        new(For(options)) { Converters = { new TrustedActorJsonConverter() } };

    public static T RoundTrip<T>(T value, string options) => RoundTrip(value, For(options));

    public static T RoundTrip<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;
}
#pragma warning restore IL2026

public sealed record Holder(Money Total);

public sealed record ActorHolder(Actor By);

public sealed record OrderPlaced(Money Total, string PlacedBy, Actor Customer, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

public sealed record OrderArchived(Actor ArchivedBy, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
