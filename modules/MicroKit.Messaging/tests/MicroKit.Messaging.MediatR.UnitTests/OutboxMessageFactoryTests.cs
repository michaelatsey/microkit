namespace MicroKit.Messaging.MediatR.UnitTests;

public sealed class OutboxMessageFactoryTests
{
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IExecutionContext _ctx = Substitute.For<IExecutionContext>();
    private readonly OutboxMessageFactory _sut;

    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    public OutboxMessageFactoryTests()
    {
        _serializer.Serialize(Arg.Any<object>()).Returns(x => $"{{\"$t\":\"{x.Arg<object>().GetType().Name}\"}}");
        _sut = new OutboxMessageFactory(_serializer, new FixedTimeProvider(Now));
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
        _serializer.Serialize(payload).Returns("{\"serialized\":true}");

        var msg = _sut.CreateNotification(payload, Guid.NewGuid(), DateTimeOffset.UtcNow, _ctx);

        msg.Payload.ShouldBe("{\"serialized\":true}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeNotification : IDomainEventNotification<FakeDomainEvent>
    {
        public FakeDomainEvent DomainEvent { get; } = new();
    }

    private sealed class FakeDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

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

    /// <summary>Minimal fixed clock — this project does not reference the testing TimeProvider.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
