namespace MicroKit.Messaging.UnitTests.Dispatch;

using MicroKit.Messaging.Dispatch;

/// <summary>
/// The standard dispatcher: a contract row becomes an envelope, every field taken from the row,
/// and anything it cannot route says so in the vocabulary the processor understands.
/// </summary>
public sealed class TransportOutboxDispatcherTests
{
    private readonly RecordingMessageTransport _transport = new();

    private TransportOutboxDispatcher Build()
        => new(_transport, NullLogger<TransportOutboxDispatcher>.Instance);

    // -----------------------------------------------------------------------------------------
    // Building the envelope
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task DispatchAsync_TakesEveryEnvelopeFieldFromTheOutboxRow()
    {
        var message = OutboxFixtures.ContractMessage();

        await Build().DispatchAsync(message);

        var envelope = _transport.Sent.ShouldHaveSingleItem();
        envelope.MessageId.ShouldBe(message.Id.Value);
        envelope.ContractName.ShouldBe(message.ContractName);
        envelope.Source.ShouldBe(message.Source);
        envelope.Payload.ShouldBe(message.Payload);
        envelope.TenantId.ShouldBe(message.TenantId);
        envelope.CorrelationId.ShouldBe(message.CorrelationId!.Value);
        envelope.CausationId.ShouldBe(message.CausationId!.Value);
        envelope.OccurredOnUtc.ShouldBe(message.OccurredOnUtc);
    }

    /// <summary>
    /// The consumer's idempotency key is the ROW's identity, and it must be stable across every
    /// redelivery of that row.
    /// </summary>
    /// <remarks>
    /// The payload here carries a deliberately different identifier, so the assertion fails if
    /// anyone ever reintroduces "read the id off the deserialized event". That source only survived
    /// a retry by coincidence — the same payload happens to deserialize to the same value, but
    /// nothing guaranteed it — and closing it was the point of ADR-MSG-018 on the in-process path.
    /// The twin assertion lived in <c>InProcessIntegrationDispatcherTests</c>, deleted with the
/// in-process fan-out (ADR-MSG-019). Its surviving counterpart is
/// <c>MediatROutboxDispatcherTests.DispatchAsync_WhenKindIsContract_NeverTouchesTheSerializer</c>.
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_UsesTheRowIdAsTheMessageId()
    {
        var otherIdentity = Guid.NewGuid();
        var message = OutboxFixtures.ContractMessage(
            payload: $$"""{"messageId":"{{otherIdentity}}"}""");

        await Build().DispatchAsync(message);

        var envelope = _transport.Sent.ShouldHaveSingleItem();
        envelope.MessageId.ShouldBe(message.Id.Value);
        envelope.MessageId.ShouldNotBe(otherIdentity);
    }

    /// <summary>
    /// <c>Source</c> is the one staged on the row, never one resolved at dispatch.
    /// </summary>
    /// <remarks>
    /// The twin of the assertion above and the same class of bug. Reading the emitting module from
    /// <c>IntegrationEventRegistry</c> when the message is sent looks equivalent and is not: it
    /// would make the value a function of the composition running <i>now</i> rather than of what
    /// was staged, so a row staged before a module was renamed would travel under the new name.
    /// That is the defect ADR-MSG-018 removed by sourcing every field from the row.
    /// <para>
    /// The row here carries a source no lookup for this contract could return, because the module
    /// has since been renamed — so an implementation that resolved it would produce something else
    /// and fail here, where <c>DispatchAsync_TakesEveryEnvelopeFieldFromTheOutboxRow</c> would stay
    /// green. The structural half of the proof is the constructor: <c>Build()</c> passes no
    /// registry because the type has no such parameter, so reintroducing one breaks this file at
    /// compile time rather than silently.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_UsesTheStagedSource_NotOneResolvedAtDispatch()
    {
        const string StagedBeforeTheRename = "/shop/order-management";
        var message = OutboxFixtures.ContractMessage(source: StagedBeforeTheRename);

        await Build().DispatchAsync(message);

        _transport.Sent.ShouldHaveSingleItem().Source.ShouldBe(StagedBeforeTheRename);
    }

    /// <summary>
    /// Nothing on the send path deserializes the payload — it travels opaque.
    /// </summary>
    /// <remarks>
    /// The structural half of this proof is the constructor: <c>Build()</c> passes no
    /// <c>IMessageSerializer</c>, because the type has no such parameter. Adding one back would
    /// break every test in this file at compile time rather than silently. The behavioural half is
    /// below — a payload that is not valid JSON at all still reaches the transport unchanged,
    /// which could not happen if anything between here and there parsed it.
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_DoesNotDeserializeThePayload()
    {
        const string NotEvenJson = "}{ this is not json at all";
        var message = OutboxFixtures.ContractMessage(payload: NotEvenJson);
        message.EventType = "Nonexistent.Type, Nonexistent.Assembly";

        await Build().DispatchAsync(message);

        _transport.Sent.ShouldHaveSingleItem().Payload.ShouldBe(NotEvenJson);
    }

    [Fact]
    public async Task DispatchAsync_WhenTenantIsNull_SendsANullTenantRatherThanInventingOne()
    {
        var message = OutboxFixtures.ContractMessage(tenantId: null);

        await Build().DispatchAsync(message);

        _transport.Sent.ShouldHaveSingleItem().TenantId.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------------------
    // Routing
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A notification with no MediatR glue registered is a CONFIGURATION fault, not a poison
    /// payload.
    /// </summary>
    /// <remarks>
    /// The distinction decides whether a one-line omission in a composition root is recoverable.
    /// <c>OutboxPayloadException</c> dead-letters on first sight, so it would silently dead-letter
    /// every domain event in the system on the first poll.
    /// <c>OutboxConfigurationException</c> releases the batch untouched and stops the worker, so
    /// the rows survive and deploying the missing package drains them.
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_WhenKindIsNotification_ThrowsOutboxConfigurationException()
    {
        var message = OutboxFixtures.Message();
        message.MessageKind = MessageKind.Notification;

        var ex = await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await Build().DispatchAsync(message));

        ex.Message.ShouldContain("AddMediatRDomainEvents");
    }

    /// <summary>
    /// The negative half, and the one that matters: nothing was handed to the transport.
    /// </summary>
    /// <remarks>
    /// Asserted against the recording fake's own list rather than with
    /// <c>DidNotReceive().SendAsync(...)</c>. A negative assertion on a mock only holds if it names
    /// a method the subject actually calls; against code that calls something else it is green
    /// whatever happens, and no mutation exposes it.
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_WhenKindIsNotification_SendsNothing()
    {
        var message = OutboxFixtures.Message();
        message.MessageKind = MessageKind.Notification;

        await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await Build().DispatchAsync(message));

        _transport.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchAsync_WhenContractNameIsMissing_ThrowsOutboxPayloadException(
        string? contractName)
    {
        var message = OutboxFixtures.ContractMessage(contractName: contractName);

        var ex = await Should.ThrowAsync<OutboxPayloadException>(
            async () => await Build().DispatchAsync(message));

        ex.Message.ShouldContain("ContractName");
        _transport.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchAsync_WhenSourceIsMissing_ThrowsOutboxPayloadException(string? source)
    {
        var message = OutboxFixtures.ContractMessage(source: source);

        var ex = await Should.ThrowAsync<OutboxPayloadException>(
            async () => await Build().DispatchAsync(message));

        ex.Message.ShouldContain("Source");
        _transport.Sent.ShouldBeEmpty();
    }

    /// <summary>
    /// An unrecognised kind dead-letters, which is the OPPOSITE of the notification arm.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate. A notification is a kind this build understands and cannot
    /// serve — fixable by deploying a package, so the rows must survive. An unknown kind cannot be
    /// interpreted at all and re-reading the row will never change that, so it is permanent for
    /// this deployment. The cast is not contrived: <c>MessageKind</c> is persisted as a string, so
    /// a later build or a hand edit produces exactly this.
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_WhenMessageKindIsUnknown_ThrowsOutboxPayloadException()
    {
        var message = OutboxFixtures.ContractMessage();
        message.MessageKind = (MessageKind)99;

        await Should.ThrowAsync<OutboxPayloadException>(
            async () => await Build().DispatchAsync(message));

        _transport.Sent.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------------
    // The transport's own failures pass through untouched
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The dispatcher does not wrap, retype or absorb what the transport throws.
    /// </summary>
    /// <remarks>
    /// A transport's exception type IS its failure classification, and the processor routes the
    /// whole batch on it. A <c>try/catch</c> here — even one that only logged and rethrew as
    /// something tidier — would destroy the only signal the engine has, turning a released batch
    /// into a dead-lettered queue.
    /// </remarks>
    [Fact]
    public Task DispatchAsync_PropagatesTransportUnavailable()
        => ShouldPropagate(new OutboxTransportUnavailableException("broker down"));

    [Fact]
    public Task DispatchAsync_PropagatesPayloadRejection()
        => ShouldPropagate(new OutboxPayloadException("message exceeds the broker's size limit"));

    /// <summary>The unclassified case — it must stay unclassified, so the processor retries it.</summary>
    [Fact]
    public Task DispatchAsync_PropagatesAnUnclassifiedFailure()
        => ShouldPropagate(new InvalidOperationException("channel hiccup"));

    private static async Task ShouldPropagate<TException>(TException fault)
        where TException : Exception
    {
        var dispatcher = new TransportOutboxDispatcher(
            new RecordingMessageTransport(_ => fault),
            NullLogger<TransportOutboxDispatcher>.Instance);

        var thrown = await Should.ThrowAsync<TException>(
            async () => await dispatcher.DispatchAsync(OutboxFixtures.ContractMessage()));

        // Same instance, not merely the same type: a dispatcher that caught and rethrew a new
        // exception of the right type would lose the transport's own message and inner exception,
        // and would pass a type-only assertion.
        thrown.ShouldBeSameAs(fault);
    }
}
