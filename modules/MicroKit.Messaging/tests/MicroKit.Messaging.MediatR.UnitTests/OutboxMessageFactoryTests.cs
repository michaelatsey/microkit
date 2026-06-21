namespace MicroKit.Messaging.MediatR.UnitTests;

public sealed class OutboxMessageFactoryTests
{
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IExecutionContext _ctx = Substitute.For<IExecutionContext>();
    private readonly OutboxMessageFactory _sut;

    public OutboxMessageFactoryTests()
    {
        _serializer.Serialize(Arg.Any<object>()).Returns(x => $"{{\"$t\":\"{x.Arg<object>().GetType().Name}\"}}");
        _sut = new OutboxMessageFactory(_serializer);
    }

    [Fact]
    public void Create_EventType_IsAssemblyQualifiedNameOfPayloadRuntimeType()
    {
        var payload = new FakeNotification();
        var msg = _sut.Create(payload, Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.EventType.ShouldBe(typeof(FakeNotification).AssemblyQualifiedName);
    }

    [Fact]
    public void Create_MessageId_CopiedFromParameter()
    {
        var id = Guid.NewGuid();
        var msg = _sut.Create(new FakeNotification(), id, DateTimeOffset.UtcNow, _ctx);

        msg.Id.ShouldBe(MessageId.From(id));
    }

    [Fact]
    public void Create_OccurredOnUtc_CopiedFromParameter()
    {
        var occurred = new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), occurred, _ctx);

        msg.OccurredOnUtc.ShouldBe(occurred);
    }

    [Fact]
    public void Create_TenantId_WhenContextHasTenantId_Copied()
    {
        _ctx.TenantId.Returns("tenant-42");
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.TenantId.ShouldBe("tenant-42");
    }

    [Fact]
    public void Create_TenantId_WhenContextTenantIdIsNull_IsNull()
    {
        _ctx.TenantId.Returns((string?)null);
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.TenantId.ShouldBeNull();
    }

    [Fact]
    public void Create_CorrelationId_WhenContextHasValidGuid_Parsed()
    {
        var guid = Guid.NewGuid();
        _ctx.CorrelationId.Returns(guid.ToString());
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CorrelationId.ShouldBe(CorrelationId.From(guid));
    }

    [Fact]
    public void Create_CorrelationId_WhenContextCorrelationIdIsNull_GeneratesNew()
    {
        _ctx.CorrelationId.Returns((string?)null);
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CorrelationId.ShouldNotBe(default);
    }

    [Fact]
    public void Create_CausationId_WhenContextHasValidGuid_Parsed()
    {
        var guid = Guid.NewGuid();
        _ctx.CausationId.Returns(guid.ToString());
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CausationId.ShouldBe(CausationId.From(guid));
    }

    [Fact]
    public void Create_CausationId_WhenContextCausationIdIsNull_IsNull()
    {
        _ctx.CausationId.Returns((string?)null);
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.CausationId.ShouldBeNull();
    }

    [Fact]
    public void Create_Status_IsPending()
    {
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.Status.ShouldBe(OutboxMessageStatus.Pending);
    }

    [Fact]
    public void Create_RetryCount_IsZero()
    {
        var msg = _sut.Create(new FakeNotification(), Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.RetryCount.ShouldBe(0);
    }

    [Fact]
    public void Create_Payload_DelegatesTo_Serializer()
    {
        var payload = new FakeNotification();
        _serializer.Serialize(payload).Returns("{\"serialized\":true}");

        var msg = _sut.Create(payload, Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.Payload.ShouldBe("{\"serialized\":true}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeNotification : IDomainEventNotification<FakeDomainEvent>
    {
        public FakeDomainEvent DomainEvent { get; } = new();
    }

    private sealed class FakeDomainEvent : IDomainEvent, IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }
}
