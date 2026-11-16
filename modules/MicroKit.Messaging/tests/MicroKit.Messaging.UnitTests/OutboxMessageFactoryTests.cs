using Microsoft.Extensions.Time.Testing;
using MicroKit.Messaging.Outbox;

namespace MicroKit.Messaging.UnitTests;

public sealed class OutboxMessageFactoryTests
{
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IExecutionContext _ctx = Substitute.For<IExecutionContext>();
    private readonly OutboxMessageFactory _sut;

    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    public OutboxMessageFactoryTests()
    {
        _serializer.ContentType.Returns("application/json");
        _serializer.Serialize(Arg.Any<object>(), Arg.Any<Type>())
            .Returns(x => (ReadOnlyMemory<byte>)System.Text.Encoding.UTF8.GetBytes(
                $"{{\"$t\":\"{x.Arg<object>().GetType().Name}\"}}"));
        _sut = new OutboxMessageFactory(_serializer, new FakeTimeProvider(Now));
    }

    [Fact]
    public void CreateNotification_EventType_IsAssemblyQualifiedNameOfPayloadRuntimeType()
    {
        var payload = new FakeNotification();
        var msg = _sut.CreateNotification(payload, Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.EventType.ShouldBe(typeof(FakeNotification).AssemblyQualifiedName);
    }

    [Fact]
    public void CreateNotification_MessageId_CopiedFromParameter()
    {
        var id = Guid.NewGuid();
        var msg = _sut.CreateNotification(new FakeNotification(), id, DateTimeOffset.UtcNow, _ctx);

        msg.Id.ShouldBe(MessageId.From(id));
    }

    [Fact]
    public void CreateNotification_OccurredOnUtc_CopiedFromParameter()
    {
        var occurred = new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), occurred, _ctx);

        msg.OccurredOnUtc.ShouldBe(occurred);
    }

    [Fact]
    public void CreateNotification_TenantId_WhenContextHasTenantId_Copied()
    {
        _ctx.TenantId.Returns("tenant-42");
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.TenantId.ShouldBe("tenant-42");
    }

    [Fact]
    public void CreateNotification_TenantId_WhenContextTenantIdIsNull_IsNull()
    {
        _ctx.TenantId.Returns((string?)null);
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.TenantId.ShouldBeNull();
    }

    [Fact]
    public void CreateNotification_CorrelationId_WhenContextHasValidGuid_Parsed()
    {
        var guid = Guid.NewGuid();
        _ctx.CorrelationId.Returns(guid.ToString());
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CorrelationId.ShouldBe(CorrelationId.From(guid));
    }

    [Fact]
    public void CreateNotification_CorrelationId_WhenContextCorrelationIdIsNull_GeneratesNew()
    {
        _ctx.CorrelationId.Returns((string?)null);
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CorrelationId.ShouldNotBe(default);
    }

    [Fact]
    public void CreateNotification_CausationId_WhenContextHasValidGuid_Parsed()
    {
        var guid = Guid.NewGuid();
        _ctx.CausationId.Returns(guid.ToString());
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CausationId.ShouldBe(CausationId.From(guid));
    }

    [Fact]
    public void CreateNotification_CausationId_WhenContextCausationIdIsNull_IsNull()
    {
        _ctx.CausationId.Returns((string?)null);
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CausationId.ShouldBeNull();
    }

    [Fact]
    public void CreateNotification_Status_IsPending()
    {
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.Status.ShouldBe(OutboxMessageStatus.Pending);
    }

    [Fact]
    public void CreateNotification_RetryCount_IsZero()
    {
        var msg = _sut.CreateNotification(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.RetryCount.ShouldBe(0);
    }

    [Fact]
    public void CreateNotification_Payload_DelegatesTo_Serializer()
    {
        var payload = new FakeNotification();
        // Serialized as its RUNTIME type, and the bytes land on the row as UTF-8 text (#186).
        _serializer.Serialize(payload, typeof(FakeNotification))
            .Returns((ReadOnlyMemory<byte>)"{\"serialized\":true}"u8.ToArray());

        var msg = _sut.CreateNotification(payload, Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.Payload.ShouldBe("{\"serialized\":true}");
    }

    [Fact]
    public void CreateContract_WithANonJsonSerializer_RefusesRatherThanCorruptingTheRow()
    {
        // The row stores the body as JSON text and the dispatcher sends it as application/json:
        // decoding a binary body as UTF-8 would corrupt it in silence.
        var binary = Substitute.For<IMessageSerializer>();
        binary.ContentType.Returns("application/x-msgpack");
        var sut = new OutboxMessageFactory(binary, new FakeTimeProvider(Now));

        var ex = Should.Throw<OutboxConfigurationException>(() => sut.CreateContract(
            new FakeNotification(), MessageId.New(), "c.v1", "/s", Now, new Dictionary<string, string>()));

        ex.Message.ShouldContain("application/x-msgpack");
        binary.DidNotReceiveWithAnyArgs().Serialize(default!, default!);
    }

    // ── contract rows: what the bus enlists in a store (#188) ─────────────────

    /// <summary>
    /// A contract row carries exactly what a direct send would have put on the envelope: the
    /// identity the caller was returned, the wire type, the source, and the publication time.
    /// </summary>
    [Fact]
    public void CreateContract_CarriesTheIdentityTheWireTypeTheSourceAndThePublicationTime()
    {
        var id = MessageId.New();
        var published = Now.AddMinutes(-3);

        var row = _sut.CreateContract(
            new FakeNotification(), id, "shop.order-placed.v1", "shop.orders", published, new Dictionary<string, string>());

        row.Id.ShouldBe(id);
        row.MessageKind.ShouldBe(MessageKind.Contract);
        row.ContractName.ShouldBe("shop.order-placed.v1");
        row.Source.ShouldBe("shop.orders");
        row.OccurredOnUtc.ShouldBe(published);
        row.CreatedAtUtc.ShouldBe(Now, "the claim orders on the staging clock");
        row.EventType.ShouldBe(typeof(FakeNotification).AssemblyQualifiedName);
        row.Payload.ShouldBe("{\"$t\":\"FakeNotification\"}");
        row.Status.ShouldBe(OutboxMessageStatus.Pending);
    }

    /// <summary>
    /// The reserved headers become the row's columns and only the others its
    /// <see cref="OutboxMessage.Headers"/>, so a header stored there can never forge them.
    /// </summary>
    [Fact]
    public void CreateContract_SplitsTheReservedHeadersFromThePublicationsOwn()
    {
        var correlation = Guid.NewGuid();
        var causation = Guid.NewGuid();
        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.TenantId] = "tenant-a",
            [MessageHeaders.CorrelationId] = correlation.ToString(),
            [MessageHeaders.CausationId] = causation.ToString(),
            [MessageHeaders.TraceParent] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ["x-origin"] = "checkout",
        };

        var row = _sut.CreateContract(new FakeNotification(), MessageId.New(), "c.v1", "/s", Now, headers);

        row.TenantId.ShouldBe("tenant-a");
        row.CorrelationId.ShouldBe(CorrelationId.From(correlation));
        row.CausationId.ShouldBe(CausationId.From(causation));
        row.TraceParent.ShouldBe("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        row.Headers.ShouldNotBeNull().ShouldBe(new Dictionary<string, string> { ["x-origin"] = "checkout" });
    }

    [Fact]
    public void CreateContract_WithNoHeadersOfItsOwn_StoresNone()
    {
        var row = _sut.CreateContract(
            new FakeNotification(), MessageId.New(), "c.v1", "/s", Now,
            new Dictionary<string, string> { [MessageHeaders.CorrelationId] = Guid.NewGuid().ToString() });

        row.Headers.ShouldBeNull();
        row.TenantId.ShouldBeNull();
        row.CausationId.ShouldBeNull("a root publication has no cause");
    }

    /// <summary>
    /// The column is required: an ambient correlation that is not a GUID is replaced, never null.
    /// </summary>
    [Fact]
    public void CreateContract_WhenTheCorrelationIsNotAGuid_SubstitutesAFreshOne()
    {
        var row = _sut.CreateContract(
            new FakeNotification(), MessageId.New(), "c.v1", "/s", Now,
            new Dictionary<string, string> { [MessageHeaders.CorrelationId] = "not-a-guid" });

        row.CorrelationId.ShouldNotBeNull();
        row.CorrelationId.Value.ShouldNotBe(Guid.Empty);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeNotification;

    /// <summary>
    /// <c>CreatedAtUtc</c> comes from the injected clock, not from <c>DateTimeOffset.UtcNow</c>.
    /// </summary>
    /// <remarks>
    /// It is the claim's sort key, so a factory that cannot be driven by a test clock makes every
    /// assertion about dispatch order depend on wall-clock resolution.
    /// </remarks>
    [Fact]
    public void CreateNotification_CreatedAtUtc_ComesFromTheInjectedClock()
    {
        var msg = _sut.CreateNotification(
            new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CreatedAtUtc.ShouldBe(Now);
    }
}
