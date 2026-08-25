namespace MicroKit.Messaging.Publishing;

using System.Diagnostics;

/// <summary>
/// Default <see cref="IIntegrationEventPublisher"/>: resolves the contract, captures the execution
/// context, serializes the payload, stages the row.
/// </summary>
/// <remarks>
/// <para>
/// Every step is a decision a caller would otherwise make itself — contract name, source, tenant,
/// correlation, causation, trace context, timestamps, serialization. Four modules making those
/// decisions independently is four chances to diverge, and the divergence would only surface at a
/// consumer.
/// </para>
/// <para>
/// <b><c>IExecutionContext</c> is injected directly, not through an accessor.</b> That is the
/// module's mechanism, not a preference: the per-message execution scope writes the message-row
/// context into a scoped holder that <c>IExecutionContext</c> resolves through, so constructor
/// injection sees it. There is no accessor type and no ambient <c>Current</c> to reach for.
/// </para>
/// <para>
/// No storage dependency: it stages through a port and is unit-testable with a fake.
/// </para>
/// </remarks>
internal sealed class IntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IIntegrationEventWriter _writer;
    private readonly IntegrationEventRegistry _registry;
    private readonly IMessageSerializer _serializer;
    private readonly IExecutionContext _executionContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntegrationEventPublisher> _logger;

    /// <summary>Initializes a new <see cref="IntegrationEventPublisher"/>.</summary>
    public IntegrationEventPublisher(
        IIntegrationEventWriter writer,
        IntegrationEventRegistry registry,
        IMessageSerializer serializer,
        IExecutionContext executionContext,
        TimeProvider timeProvider,
        ILogger<IntegrationEventPublisher> logger)
    {
        _writer = writer;
        _registry = registry;
        _serializer = serializer;
        _executionContext = executionContext;
        _timeProvider = timeProvider;
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

        // Guard BEFORE any other work, and the order is the contract. A row staged with no
        // transaction to commit it is not an error anyone ever sees: the change tracker is
        // discarded, the event never existed, and no log, metric or trace records that it was
        // meant to. A guard placed after staging would leave the row for whatever transaction the
        // caller happened to have.
        if (!_writer.HasOpenTransaction)
        {
            throw new IntegrationEventPublishException(
                $"'{typeof(TEvent).Name}' was published with no open transaction. Staging is only " +
                "meaningful inside a unit of work the caller opened: without one the row goes to a " +
                "change tracker nobody saves, and the event silently never existed. Note that " +
                "IUnitOfWork.CommitAsync alone is not enough — it is a bare SaveChangesAsync whose " +
                "implicit transaction never appears as an open one. Wrap the work in " +
                "ITransactionalContext.ExecuteAsync and publish inside that callback.");
        }

        // GetType(), not typeof(TEvent): a caller holding the interface would otherwise resolve the
        // contract of IIntegrationEvent itself, which is registered nowhere.
        var registration = _registry.ResolveContract(integrationEvent.GetType());

        var message = new IntegrationEventMessage
        {
            Id = MessageId.New(),
            ContractName = registration.ContractName,
            Source = registration.Source,
            Data = _serializer.Serialize(integrationEvent),

            // The context carries strings, so the strong types are rebuilt here. An unparseable
            // value becomes null rather than throwing: a malformed correlation id is a tracing
            // defect, and losing an integration event over one would be the wrong trade.
            TenantId = _executionContext.TenantId,
            CorrelationId = ParseCorrelation(_executionContext.CorrelationId),
            CausationId = ParseCausation(_executionContext.CausationId),

            // Captured now because this is the last moment the producing trace is current. The
            // relay runs later, on another thread, under another activity; without the captured
            // parent the consumer's work shows up as an unrelated trace, and the chain breaks
            // exactly where crossing an asynchronous boundary makes it most valuable.
            TraceParent = Activity.Current?.Id,

            CreatedAtUtc = _timeProvider.GetUtcNow(),
            OccurredOnUtc = occurredOnUtc,

            Status = IntegrationEventStatus.Pending,
            DeadLettered = false,
            RetryCount = 0,
        };

        await _writer.AddAsync(message, ct).ConfigureAwait(false);

        IntegrationEventLogs.Staged(
            _logger, message.Id.Value, message.ContractName, message.Source, message.TenantId);

        if (occurredOnUtc is null)
        {
            IntegrationEventLogs.OccurrenceTimeNotSupplied(
                _logger, message.Id.Value, message.ContractName);
        }

        return message.Id;
    }

    private static CorrelationId? ParseCorrelation(string? value) =>
        Guid.TryParse(value, out var guid) ? CorrelationId.From(guid) : null;

    private static CausationId? ParseCausation(string? value) =>
        Guid.TryParse(value, out var guid) ? CausationId.From(guid) : null;
}
