namespace MicroKit.Messaging.UnitTests.Transport;

using System.Text.Json;

using MicroKit.Messaging.Serialization;

/// <summary>
/// The wire format. These tests exist because <see cref="MessageEnvelope"/> is a compatibility
/// commitment: once a message has travelled, every member is something a deployed consumer must
/// still be able to read, including one built by someone who does not share this repository.
/// </summary>
public sealed class MessageEnvelopeTests
{
    private readonly SystemTextJsonMessageSerializer _serializer = new();

    private static MessageEnvelope FullyPopulated() => new(
        MessageId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
        ContractName: "shop.orders.order-placed.v1",
        Source: "/shop/orders",
        Payload: """{"orderId":"e0b3a1f2-0000-4000-8000-000000000001"}""",
        TenantId: "tenant-a",
        CorrelationId: Guid.Parse("22222222-2222-4222-8222-222222222222"),
        CausationId: Guid.Parse("33333333-3333-4333-8333-333333333333"),
        OccurredOnUtc: new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Pins the wire shape by name. A rename here is a breaking change to every deployed consumer
    /// at once, so it must fail a test rather than pass a review — the same reason the contract
    /// registry's names are snapshot-tested.
    /// </summary>
    /// <remarks>
    /// The exact set is asserted, not merely a subset: an assertion that only checks the expected
    /// names are present would stay green if a member were <i>added</i>, and an accidental addition
    /// is a commitment too. It also pins the camelCase that <c>JsonSerializerDefaults.Web</c>
    /// produces, which is part of the format whether or not anyone chose it deliberately.
    /// </remarks>
    [Fact]
    public void MessageEnvelope_SerializesToTheDeclaredWireShape()
    {
        var json = _serializer.Serialize(FullyPopulated());

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();

        names.ShouldBe(
        [
            "causationId",
            "contractName",
            "correlationId",
            "messageId",
            "occurredOnUtc",
            "payload",
            "source",
            "tenantId",
        ]);
    }

    /// <summary>
    /// The identifiers travel as bare strings, not as <c>{"value":"…"}</c> wrappers.
    /// </summary>
    /// <remarks>
    /// Pinned separately from the name check because it is a different failure: retyping these
    /// members back to <c>MessageId</c> / <c>CorrelationId</c> / <c>CausationId</c> would keep every
    /// property name intact and silently nest the values one level deeper, which no deployed
    /// consumer could absorb. The names test would stay green through exactly that change.
    /// </remarks>
    [Fact]
    public void MessageEnvelope_CarriesIdentifiersAsBareStrings()
    {
        var json = _serializer.Serialize(FullyPopulated());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("messageId").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("messageId").GetString()
            .ShouldBe("11111111-1111-4111-8111-111111111111");
        root.GetProperty("correlationId").GetString()
            .ShouldBe("22222222-2222-4222-8222-222222222222");
        root.GetProperty("causationId").GetString()
            .ShouldBe("33333333-3333-4333-8333-333333333333");
    }

    /// <summary>
    /// The single-tenant, no-correlation, root-event case: the one a deployment without Tenancy
    /// actually produces.
    /// </summary>
    /// <remarks>
    /// Proves the optional members are optional <i>on the wire</i> and not merely nullable in C#.
    /// A serializer configuration that refused null, or a receiver that required every member,
    /// would fail here rather than in the first single-tenant deployment.
    /// </remarks>
    [Fact]
    public void MessageEnvelope_RoundTripsWithEveryOptionalFieldNull()
    {
        var envelope = FullyPopulated() with
        {
            TenantId = null,
            CorrelationId = null,
            CausationId = null,
        };

        var json = _serializer.Serialize(envelope);
        var restored = _serializer.Deserialize(json, typeof(MessageEnvelope).AssemblyQualifiedName!);

        restored.ShouldBeOfType<MessageEnvelope>().ShouldBe(envelope);
    }

    [Fact]
    public void MessageEnvelope_RoundTripsEveryPopulatedField()
    {
        var envelope = FullyPopulated();

        var json = _serializer.Serialize(envelope);
        var restored = _serializer.Deserialize(json, typeof(MessageEnvelope).AssemblyQualifiedName!);

        restored.ShouldBeOfType<MessageEnvelope>().ShouldBe(envelope);
    }

    /// <summary>
    /// The payload is opaque: it survives the envelope byte for byte, never re-serialized.
    /// </summary>
    /// <remarks>
    /// The payload here is deliberately awkward — it contains a quote, a backslash and a non-ASCII
    /// character. A round trip that re-parsed and re-emitted it would still produce <i>equivalent</i>
    /// JSON, so a well-behaved sample would pass either way; this one changes if anything touches
    /// the string.
    /// </remarks>
    [Fact]
    public void MessageEnvelope_PayloadIsCarriedVerbatim()
    {
        const string Payload = """{"note":"a \"quoted\" value \\ with an accent: é","n":1.50}""";
        var envelope = FullyPopulated() with { Payload = Payload };

        var json = _serializer.Serialize(envelope);
        var restored = (MessageEnvelope)_serializer
            .Deserialize(json, typeof(MessageEnvelope).AssemblyQualifiedName!)!;

        restored.Payload.ShouldBe(Payload);
    }
}
