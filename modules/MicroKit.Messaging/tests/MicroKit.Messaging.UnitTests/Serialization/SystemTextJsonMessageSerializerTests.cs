namespace MicroKit.Messaging.UnitTests.Serialization;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.Options;

public sealed class SystemTextJsonMessageSerializerTests
{
    private readonly SystemTextJsonMessageSerializer _sut = new();

    [Fact]
    public void Serialize_ThenDeserialize_ReturnsEquivalentEvent()
    {
        var evt = new TestOrderPlacedEvent(
            MessageId: new MessageId(Guid.NewGuid()),
            TenantId: "tenant-1",
            OrderId: Guid.NewGuid(),
            Amount: 99.99m);

        var body = _sut.Serialize(evt, evt.GetType());
        var deserialized = _sut.Deserialize(body, typeof(TestOrderPlacedEvent));

        var typed = deserialized.ShouldBeOfType<TestOrderPlacedEvent>();
        typed.MessageId.ShouldBe(evt.MessageId);
        typed.TenantId.ShouldBe(evt.TenantId);
        typed.OrderId.ShouldBe(evt.OrderId);
        typed.Amount.ShouldBe(evt.Amount);
    }

    [Fact]
    public void Serialize_AsTheRuntimeType_KeepsConcreteMembers_WhenTheStaticTypeIsAnInterface()
    {
        IIntegrationEvent evt = new TestOrderPlacedEvent(
            MessageId: new MessageId(Guid.NewGuid()),
            TenantId: "tenant-1",
            OrderId: Guid.NewGuid(),
            Amount: 42.00m);

        var json = Encoding.UTF8.GetString(_sut.Serialize(evt, evt.GetType()).Span);

        json.ShouldContain("orderId");
        json.ShouldContain("amount");
    }

    [Fact]
    public void ContentType_IsApplicationJson()
        => _sut.ContentType.ShouldBe("application/json");

    [Fact]
    public void Deserialize_WhenBodyMalformed_Throws()
    {
        // The contract allows a throw or a null; this implementation throws, so the caller keeps
        // the parser's message as the dead-letter reason.
        Should.Throw<JsonException>(
            () => _sut.Deserialize("{{not valid json{{"u8.ToArray(), typeof(TestOrderPlacedEvent)));
    }

    [Fact]
    public void AHostConverter_IsUsedInBothDirections()
    {
        var converter = new CountingMoneyConverter();
        var sut = Build(o => o.Converters.Add(converter));

        var json = Encoding.UTF8.GetString(sut.Serialize(new Priced(new Money(12)), typeof(Priced)).Span);
        var restored = sut.Deserialize(Encoding.UTF8.GetBytes(json), typeof(Priced));

        json.ShouldContain("\"EUR:12\"");
        restored.ShouldBeOfType<Priced>().Price.ShouldBe(new Money(12));
        converter.Writes.ShouldBe(1);
        converter.Reads.ShouldBe(1);
    }

    [Fact]
    public void AHostJsonContext_InTheResolverChain_KeepsReflectionAsTheFallback()
    {
        // The context declares Priced only. Without the appended reflection resolver every other
        // message type would stop serializing the moment a host adds a context.
        var sut = Build(o => o.TypeInfoResolverChain.Insert(0, PricedJsonContext.Default));

        var undeclared = new TestOrderPlacedEvent(MessageId.New(), "t", Guid.NewGuid(), 1m);
        var json = Encoding.UTF8.GetString(sut.Serialize(undeclared, undeclared.GetType()).Span);

        json.ShouldContain("orderId");
        Encoding.UTF8.GetString(sut.Serialize(new Priced(new Money(3)), typeof(Priced)).Span)
            .ShouldContain("price");
    }

    [Fact]
    public void RowEventTypes_WhenTheNameResolvesToNothing_ReturnsNull()
        => RowEventTypes.Resolve("NonExistent.Type.DoesNotExist, Nowhere").ShouldBeNull();

    private static SystemTextJsonMessageSerializer Build(Action<JsonSerializerOptions> configure)
    {
        var options = new MessagingJsonOptions();
        configure(options.SerializerOptions);
        return new SystemTextJsonMessageSerializer(Options.Create(options));
    }
}

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

internal sealed record TestOrderPlacedEvent(
    MessageId MessageId,
    string TenantId,
    Guid OrderId,
    decimal Amount) : IIntegrationEvent;

internal readonly record struct Money(int Amount);

internal sealed record Priced(Money Price);

/// <summary>Writes <c>Money</c> as <c>"EUR:&lt;amount&gt;"</c> and counts both directions.</summary>
internal sealed class CountingMoneyConverter : JsonConverter<Money>
{
    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Reads++;
        var text = reader.GetString() ?? throw new JsonException("Money must be a string.");
        return new Money(int.Parse(text["EUR:".Length..], System.Globalization.CultureInfo.InvariantCulture));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        Writes++;
        writer.WriteStringValue(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"EUR:{value.Amount}"));
    }
}

[JsonSerializable(typeof(Priced))]
internal sealed partial class PricedJsonContext : JsonSerializerContext;
