using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.EntityFrameworkCore.Tests;

public sealed class JsonValueConvertersTests
{
    private sealed record Address(string Street, string City);

    private static string Serialize<T>(ValueConverter<T, string> converter, T value) =>
        converter.ConvertToProviderExpression.Compile()(value);

    private static T Deserialize<T>(ValueConverter<T, string> converter, string json) =>
        converter.ConvertFromProviderExpression.Compile()(json);

    // ── DefaultOptions ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_PropertyNamingPolicy_IsCamelCase()
    {
        Assert.Equal(JsonNamingPolicy.CamelCase, JsonValueConverters.DefaultOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void DefaultOptions_WriteIndented_IsFalse()
    {
        Assert.False(JsonValueConverters.DefaultOptions.WriteIndented);
    }

    // ── Create<T> — serialize ─────────────────────────────────────────────────

    [Fact]
    public void Create_Serializes_ObjectToJson()
    {
        var converter = JsonValueConverters.Create<Address>();

        var json = Serialize(converter, new Address("123 Main St", "Springdale"));

        Assert.Contains("street", json);
        Assert.Contains("123 Main St", json);
    }

    [Fact]
    public void Create_SerializesWithCamelCase_ByDefault()
    {
        var converter = JsonValueConverters.Create<Address>();

        var json = Serialize(converter, new Address("A", "B"));

        Assert.Contains("\"street\"", json);
        Assert.Contains("\"city\"", json);
    }

    [Fact]
    public void Create_SerializesCollection()
    {
        var converter = JsonValueConverters.Create<List<string>>();

        var json = Serialize(converter, ["a", "b"]);

        Assert.Equal("[\"a\",\"b\"]", json);
    }

    // ── Create<T> — deserialize ───────────────────────────────────────────────

    [Fact]
    public void Create_Deserializes_JsonToObject()
    {
        var converter = JsonValueConverters.Create<Address>();

        var result = Deserialize(converter, "{\"street\":\"42 Oak Ave\",\"city\":\"Riverside\"}");

        Assert.Equal("42 Oak Ave", result.Street);
        Assert.Equal("Riverside", result.City);
    }

    [Fact]
    public void Create_Deserializes_Collection()
    {
        var converter = JsonValueConverters.Create<List<int>>();

        var result = Deserialize(converter, "[1,2,3]");

        Assert.Equal(new List<int> { 1, 2, 3 }, result);
    }

    [Fact]
    public void Create_RoundTrip_PreservesValue()
    {
        var converter = JsonValueConverters.Create<Address>();
        var original = new Address("99 Pine Rd", "Lakewood");

        var json = Serialize(converter, original);
        var restored = Deserialize(converter, json);

        Assert.Equal(original, restored);
    }

    // ── Create<T> — null JSON throws ─────────────────────────────────────────

    [Fact]
    public void Create_DeserializesNull_ThrowsInvalidOperationException()
    {
        var converter = JsonValueConverters.Create<Address>();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        Assert.Throws<InvalidOperationException>(() => fromProvider("null"));
    }

    // ── Create<T> — custom options ────────────────────────────────────────────

    [Fact]
    public void Create_WithCustomOptions_UsesProvidedOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        var converter = JsonValueConverters.Create<Address>(options);

        var json = Serialize(converter, new Address("1 Elm", "Oakville"));

        Assert.Contains("\"street\"", json);
        Assert.Contains("\"city\"", json);
    }

    [Fact]
    public void Create_WithNullOptions_FallsBackToDefaultOptions()
    {
        var converter = JsonValueConverters.Create<Address>(null);

        var json = Serialize(converter, new Address("5 Birch", "Maplewood"));

        Assert.Contains("\"street\"", json);
    }
}
