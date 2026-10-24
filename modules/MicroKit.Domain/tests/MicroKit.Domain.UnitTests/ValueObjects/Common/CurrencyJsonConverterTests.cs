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
public sealed class CurrencyJsonConverterTests
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
    public void Currency_RoundTrips(string options)
    {
        var currency = Currency.FromCode("KWD");

        JsonSerializer.Serialize(currency, For(options)).ShouldBe("\"KWD\"");
        RoundTrip(currency, options).ShouldBe(currency);
    }

    [Fact]
    public void Currency_UnknownCode_FailsOnDeserialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Currency>("\"ABC\""))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Fact]
    public void Currency_Default_FailsOnSerialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(default(Currency)));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void CurrencyAndActor_RoundTripAsDictionaryKeys(string options)
    {
        var totals = new Dictionary<Currency, Money>
        {
            [Currency.FromCode("EUR")] = new(10.50m, Currency.FromCode("EUR")),
            [Currency.FromCode("XOF")] = new(1500m, Currency.FromCode("XOF")),
        };
        var byActor = new Dictionary<Actor, int> { [new Actor("user-1")] = 1, [new Actor("user-2")] = 2 };
        var byTrustedActor = new Dictionary<Actor, int> { [Actor.System] = 1, [new Actor("user-1")] = 2 };

        RoundTrip(totals, options).ShouldBe(totals, ignoreOrder: true);
        RoundTrip(byActor, options).ShouldBe(byActor, ignoreOrder: true);
        // Written with default options, read back only with the trusted converter (ADR-004 D1).
        var written = JsonSerializer.Serialize(byTrustedActor, For(options));
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Actor, int>>(written, For(options)));

        var trusted = RoundTrip(byTrustedActor, Trusted(options));
        trusted[Actor.System].ShouldBe(1);
        trusted[new Actor("user-1")].ShouldBe(2);
    }
}
#pragma warning restore IL2026
