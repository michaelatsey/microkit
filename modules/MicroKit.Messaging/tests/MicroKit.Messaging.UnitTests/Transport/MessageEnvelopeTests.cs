namespace MicroKit.Messaging.UnitTests.Transport;

using System.Reflection;

/// <summary>
/// Pins the wire form of <see cref="MessageEnvelope"/> (#186): the CloudEvents-aligned member set,
/// its types, and that it can only be built through init-only members.
/// </summary>
/// <remarks>
/// The envelope is a compatibility commitment at the member level: every member below is something
/// a broker provider maps and a consumer reads. The byte encoding belongs to the provider's
/// CloudEvents binding, so the assertion is on members, not on a serialized shape.
/// </remarks>
public sealed class MessageEnvelopeTests
{
    [Fact]
    public void MessageEnvelope_ExposesTheCloudEventsAttributesHeadersAndBody()
    {
        var members = typeof(MessageEnvelope)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.PropertyType);

        members.ShouldBe(
            new Dictionary<string, Type>
            {
                ["Id"] = typeof(string),
                ["Source"] = typeof(string),
                ["Type"] = typeof(string),
                ["Time"] = typeof(DateTimeOffset),
                ["DataContentType"] = typeof(string),
                ["DataSchema"] = typeof(string),
                ["Headers"] = typeof(IReadOnlyDictionary<string, string>),
                ["Body"] = typeof(ReadOnlyMemory<byte>),
            },
            ignoreOrder: true,
            "the envelope's member set is a wire commitment: a rename or removal breaks every " +
            "provider and every deployed consumer, so change it deliberately");
    }

    [Fact]
    public void MessageEnvelope_HasNoPositionalConstructor_SoAddingAHeaderNeverChangesOne()
    {
        typeof(MessageEnvelope).IsSealed.ShouldBeTrue();
        typeof(MessageEnvelope).GetConstructors()
            .ShouldHaveSingleItem().GetParameters().ShouldBeEmpty();

        foreach (var property in typeof(MessageEnvelope).GetProperties())
        {
            var setter = property.SetMethod.ShouldNotBeNull($"{property.Name} must be init-only");
            setter.ReturnParameter.GetRequiredCustomModifiers()
                .ShouldContain(typeof(System.Runtime.CompilerServices.IsExternalInit),
                    $"{property.Name} must be init-only, not settable");
        }
    }

    [Fact]
    public void MessageEnvelope_DefaultsToNoHeadersAndNoSchema()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Source = "/shop/orders",
            Type = "shop.orders.order-placed.v1",
            Time = DateTimeOffset.UnixEpoch,
            DataContentType = "application/json",
            Body = "{}"u8.ToArray(),
        };

        envelope.Headers.ShouldBeEmpty();
        envelope.DataSchema.ShouldBeNull();
    }

    [Fact]
    public void MessageHeaders_AreTheFourLowerCaseCloudEventsExtensions()
    {
        // CloudEvents extension attribute names are lower-case alphanumeric. A name outside that
        // alphabet would not survive a binding that maps extensions to broker headers.
        string[] reserved =
        [
            MessageHeaders.TenantId,
            MessageHeaders.CorrelationId,
            MessageHeaders.CausationId,
            MessageHeaders.TraceParent,
        ];

        reserved.ShouldBe(["tenantid", "correlationid", "causationid", "traceparent"]);
        reserved.ShouldAllBe(name => MessageHeaders.IsReserved(name));
        MessageHeaders.IsReserved("x-custom").ShouldBeFalse();
        MessageHeaders.IsReserved("TenantId").ShouldBeTrue(
            "reserved names compare ignoring case: a case-insensitive binding would otherwise carry " +
            "two values for one attribute");
    }
}
