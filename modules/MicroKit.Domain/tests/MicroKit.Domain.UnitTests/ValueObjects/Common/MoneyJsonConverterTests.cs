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
public sealed class MoneyJsonConverterTests
{
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
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
    public void Money_UnknownCurrencyCode_FailsOnDeserialization(string options)
    {
        var json = options == "web"
            ? """{"amount":10,"currency":"ABC"}"""
            : """{"Amount":10,"Currency":"ABC"}""";

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json, For(options)))
            .InnerException.ShouldBeOfType<DomainException>();
    }

    [Theory]
    [MemberData(nameof(JsonTestOptions.Options), MemberType = typeof(JsonTestOptions))]
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
    public void Money_InvalidContent_ReportsTheJsonPath()
    {
        var ex = Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Holder>("""{"Total":{"Amount":1.5,"Currency":"JPY"}}"""));

        ex.Path.ShouldBe("$.Total");
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
}
#pragma warning restore IL2026
