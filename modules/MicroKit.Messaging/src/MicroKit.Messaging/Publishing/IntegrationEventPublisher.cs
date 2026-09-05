namespace MicroKit.Messaging.Publishing;

using MicroKit.Messaging.Outbox;

/// <summary>
/// Default <see cref="IIntegrationEventPublisher"/>: resolves the contract, names the dispatch that
/// produced it, builds the row and writes it into the caller's transaction.
/// </summary>
/// <remarks>
/// <para>
/// Every step is a decision a caller would otherwise make itself — contract name, source, tenant,
/// correlation, causation, trace context, timestamps, serialization. Four modules making those
/// decisions independently is four chances to diverge, and the divergence would only surface at a
/// consumer.
/// </para>
/// <para>
/// <b>It writes a <see cref="MessageKind.Contract"/> row into the outbox</b>, the same table the
/// domain-event path uses. One table, two natures, routed by the column (ADR-MSG-019) — which is
/// what lets a contract published by a notification handler make a second pass through the same
/// claim, lease, back-off and dead-letter machinery instead of a duplicate copy of it.
/// </para>
/// <para>
/// <b><c>IExecutionContext</c> is injected directly, not through an accessor.</b> That is the
/// module's mechanism, not a preference: the per-message execution scope writes the message-row
/// context into a scoped holder that <c>IExecutionContext</c> resolves through, so constructor
/// injection sees it. There is no accessor type and no ambient <c>Current</c> to reach for.
/// <see cref="OriginMessageHolder"/> travels the same way and for the same reason.
/// </para>
/// <para>
/// <b>A replay is absorbed here and the caller never learns of it.</b> A redelivered dispatch
/// re-runs its notification handlers, which publish the same contract from the same origin row; the
/// second write collides on the replay key and the pre-existing row's identifier is returned. If
/// that collision escaped instead, every notification handler would need a <c>try</c>/<c>catch</c>
/// on a database exception to survive its own redelivery.
/// </para>
/// <para>
/// No storage dependency: it writes through a port and is unit-testable with a recording fake.
/// </para>
/// </remarks>
internal sealed class IntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IIntegrationEventWriter _writer;
    private readonly IntegrationEventRegistry _registry;
    private readonly OutboxMessageFactory _factory;
    private readonly OriginMessageHolder _originHolder;
    private readonly IExecutionContext _executionContext;
    private readonly ILogger<IntegrationEventPublisher> _logger;

    /// <summary>Initializes a new <see cref="IntegrationEventPublisher"/>.</summary>
    public IntegrationEventPublisher(
        IIntegrationEventWriter writer,
        IntegrationEventRegistry registry,
        OutboxMessageFactory factory,
        OriginMessageHolder originHolder,
        IExecutionContext executionContext,
        ILogger<IntegrationEventPublisher> logger)
    {
        _writer = writer;
        _registry = registry;
        _factory = factory;
        _originHolder = originHolder;
        _executionContext = executionContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<MessageId> PublishAsync<TEvent>(
        TEvent integrationEvent,
        DateTimeOffset? occurredOnUtc = null,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Guard BEFORE any other work, and the order is the contract. Without an open transaction
        // the writer's flush would commit through the provider's implicit per-statement
        // transaction: an integration event announced permanently, for a business fact the caller
        // may still roll back. A guard placed after the write would have nothing left to prevent.
        if (!_writer.HasOpenTransaction)
        {
            throw new IntegrationEventPublishException(
                $"'{typeof(TEvent).Name}' was published with no open transaction. Publishing is " +
                "only meaningful inside a unit of work the caller opened: the row must commit — or " +
                "roll back — with the business fact it announces. Note that IUnitOfWork.CommitAsync " +
                "alone is not enough — it is a bare SaveChangesAsync whose implicit transaction " +
                "never appears as an open one. Wrap the work in ITransactionalContext.ExecuteAsync " +
                "and publish inside that callback.");
        }

        // GetType(), not typeof(TEvent): a caller holding the interface would otherwise resolve the
        // contract of IIntegrationEvent itself, which is registered nowhere.
        var registration = _registry.ResolveContract(integrationEvent.GetType());

        // Null outside a dispatch — a command handler, a scheduled job. Such a row does not
        // deduplicate, and for those two callers that is correct: an HTTP request is not replayed.
        // An inbox handler is NOT a third example of that: its replay is real, and what makes a
        // null safe there is IInboxSettlementStore's transactional settlement, not the absence of
        // a replay. See OriginMessageHolder for why the distinction matters and what the receiving
        // seam owes.
        var origin = _originHolder.OriginMessageId;

        var message = _factory.CreateContract(
            integrationEvent,
            registration.ContractName,
            registration.Source,
            origin,
            occurredOnUtc,
            _executionContext);

        var result = await _writer.AddAsync(message, ct).ConfigureAwait(false);

        if (result.AlreadyPublished)
        {
            // Debug, not Warning. Under at-least-once delivery this is the nominal path, reached
            // whenever a dispatch is redelivered; the rate is the signal, not the event.
            IntegrationEventLogs.AlreadyPublished(
                _logger,
                result.Id.Value,
                registration.ContractName,
                message.OriginMessageId?.Value);

            return result.Id;
        }

        // The origin is logged, not merely persisted. A null one is legitimate and common, and it
        // is also what a publish from the wrong scope produces — this field is the only thing that
        // separates the two after the fact. See IntegrationEventLogs.Staged.
        IntegrationEventLogs.Staged(
            _logger,
            result.Id.Value,
            registration.ContractName,
            registration.Source,
            message.TenantId,
            message.OriginMessageId?.Value);

        if (occurredOnUtc is null)
        {
            IntegrationEventLogs.OccurrenceTimeNotSupplied(
                _logger, result.Id.Value, registration.ContractName);
        }

        return result.Id;
    }
}
