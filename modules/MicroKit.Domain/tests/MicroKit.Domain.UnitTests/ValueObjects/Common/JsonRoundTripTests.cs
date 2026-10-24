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

public sealed class JsonRoundTripTests
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
    [InlineData("default", 10.50, "EUR")]
    [InlineData("default", 1500, "XOF")]
    [InlineData("default", 1.234, "TND")]
    [InlineData("web", 10.50, "EUR")]
    [InlineData("web", 1500, "XOF")]
    [InlineData("web", 1.234, "TND")]
    public void Money_RoundTrips(string options, decimal amount, string code)
    {
        var money = new Money(amount, Currency.FromCode(code));

        RoundTrip(money, options).ShouldBe(money);
    }

    [Fact]
    public void Money_DefaultOptions_WritesAmountAndCurrencyCodeOnly()
    {
        JsonSerializer.Serialize(new Money(10.50m, Currency.FromCode("EUR")))
            .ShouldBe("""{"Amount":10.50,"Currency":"EUR"}""");
    }

    [Fact]
    public void Money_WebOptions_FollowsTheNamingPolicy()
    {
        JsonSerializer.Serialize(new Money(10.50m, Currency.FromCode("EUR")), JsonSerializerOptions.Web)
            .ShouldBe("""{"amount":10.50,"currency":"EUR"}""");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Currency_RoundTrips(string options)
    {
        var currency = Currency.FromCode("KWD");

        JsonSerializer.Serialize(currency, For(options)).ShouldBe("\"KWD\"");
        RoundTrip(currency, options).ShouldBe(currency);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_ReservedValue_ThroughTheDefaultConverter_ThrowsJsonExceptionWithAPath(string options)
    {
        var json = options == "web" ? """{"by":"$system"}""" : """{"By":"$system"}""";

        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ActorHolder>(json, For(options)));

        ex.Path.ShouldBe(options == "web" ? "$.by" : "$.By");
        ex.InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltIn_WithTheDefaultConverter_FailsOnWrite(string options)
    {
        var ex = Should.Throw<JsonException>(() => JsonSerializer.Serialize(Actor.System, For(options)));

        ex.Message.ShouldContain(nameof(TrustedActorJsonConverter));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltInAsDictionaryKey_WithTheDefaultConverter_FailsOnWrite(string options)
    {
        var byActor = new Dictionary<Actor, int> { [Actor.System] = 1 };

        Should.Throw<JsonException>(() => JsonSerializer.Serialize(byActor, For(options)))
            .Message.ShouldContain(nameof(TrustedActorJsonConverter));
    }

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
    public void Actor_User_RoundTrips(string options)
    {
        var actor = new Actor("user-123");

        JsonSerializer.Serialize(actor, For(options)).ShouldBe("\"user-123\"");
        RoundTrip(actor, options).ShouldBe(actor);
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

    [Theory]
    [MemberData(nameof(Options))]
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

    [Theory]
    [MemberData(nameof(Options))]
    public void Money_UnknownCurrencyCode_FailsOnDeserialization(string options)
    {
        var json = options == "web"
            ? """{"amount":10,"currency":"ABC"}"""
            : """{"Amount":10,"Currency":"ABC"}""";

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json, For(options)))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Money_ExcessScale_FailsOnDeserialization(string options)
    {
        var json = options == "web"
            ? """{"amount":1.5,"currency":"JPY"}"""
            : """{"Amount":1.5,"Currency":"JPY"}""";

        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json, For(options)));
        ex.InnerException.ShouldBeOfType<DomainException>();
    }

    [Fact]
    public void Money_MissingCurrency_FailsOnDeserialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>("""{"Amount":10}"""));
    }

    [Fact]
    public void Currency_UnknownCode_FailsOnDeserialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Currency>("\"ABC\""))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Fact]
    public void Actor_UnknownReservedValue_FailsOnDeserialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Actor>("\"$root\""))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Fact]
    public void Money_InvalidContent_ReportsTheJsonPath()
    {
        var ex = Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Holder>("""{"Total":{"Amount":1.5,"Currency":"JPY"}}"""));

        ex.Path.ShouldBe("$.Total");
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
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(byTrustedActor, For(options)));

        var trusted = RoundTrip(byTrustedActor, Trusted(options));
        trusted[Actor.System].ShouldBe(1);
        trusted[new Actor("user-1")].ShouldBe(2);
    }

    [Fact]
    public void Currency_Default_FailsOnSerialization()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(default(Currency)));
    }

    [Fact]
    public void Money_WriteAsString_WritesTheAmountAsAString()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString };
        var money = new Money(10.50m, Currency.FromCode("EUR"));

        var json = JsonSerializer.Serialize(money, options);

        json.ShouldBe("""{"Amount":"10.50","Currency":"EUR"}""");
        JsonSerializer.Deserialize<Money>(json, options).ShouldBe(money);
    }

    [Fact]
    public void Money_QuotedAmountWithExponent_IsRead()
    {
        JsonSerializer.Deserialize<Money>("""{"Amount":"1E2","Currency":"EUR"}""", JsonSerializerOptions.Web)!
            .Amount.ShouldBe(100m);
    }

    public sealed record Holder(Money Total);

    public sealed record ActorHolder(Actor By);

    public sealed record OrderPlaced(Money Total, string PlacedBy, Actor Customer, DateTimeOffset OccurredAt)
        : DomainEvent(OccurredAt);

    public sealed record OrderArchived(Actor ArchivedBy, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
}
#pragma warning restore IL2026
