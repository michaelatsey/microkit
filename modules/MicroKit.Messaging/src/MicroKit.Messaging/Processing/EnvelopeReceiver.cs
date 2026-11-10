using System.Text;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Registry;

using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The standard <see cref="IEnvelopeReceiver"/>: resolves an envelope's contract name to this
/// process's own event type, and records one <see cref="InboxMessage"/> per registered consumer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The producing side of the inbox, and the only one.</b> Nothing else in MicroKit writes an
/// <see cref="InboxMessage"/>: the in-process fan-out that used to do it from the <i>publishing</i>
/// side was withdrawn by ADR-MSG-019, precisely because writing a consumer's row inside the
/// producer is the confusion the contract-name indirection exists to remove. Rows are written here,
/// in the receiving process, from an envelope that crossed a transport.
/// </para>
/// <para>
/// <b>Two lookups, and the first one is what makes a transport possible at all.</b>
/// <see cref="IntegrationEventRegistry.TryResolveLocalType"/> maps the wire name to a local CLR
/// type — never the producer's, whose assembly this process does not hold, which is why
/// <see cref="MessageEnvelope"/> deliberately does not carry an assembly-qualified name.
/// <c>MessageHandlerRegistry.GetHandlers</c> then maps that type to its consumers. ADR-MSG-019 kept
/// the second seam alive with no caller for exactly this step.
/// </para>
/// <para>
/// <b><c>EventType</c> is stamped from the resolved local type, not from the envelope.</b> The
/// column feeds <c>IMessageSerializer.Deserialize</c>, which calls <c>Type.GetType</c> on it during
/// the drain — so it must name a type <i>this</i> process can load. Copying a contract name there,
/// or a producer's type name, produces rows that dead-letter on the first drain with a payload that
/// was perfectly well-formed.
/// </para>
/// <para>
/// <b><c>ReceivedAtUtc</c> comes from the clock, never from
/// <see cref="MessageEnvelope.Time"/>.</b> It is the local receipt instant, and it is the
/// inbox claim's ordering key. Ordering on a caller-supplied business timestamp would let a
/// backdated event jump the whole queue — the same defect the outbox claim closed when it moved to
/// <c>CreatedAtUtc</c>.
/// </para>
/// <para>
/// <b>Both clocks are recorded, because they answer different questions.</b>
/// <see cref="MessageEnvelope.Time"/> is carried onto
/// <see cref="InboxMessage.OccurredOnUtc"/> verbatim: since ADR-MSG-018 made
/// <c>IIntegrationEvent</c> a bare marker, a payload need carry no timestamp of its own, so
/// dropping the envelope's would leave the business time nowhere on the receiving side and a
/// handler reading <c>ReceivedAtUtc</c> would be reading the relay's clock for a business fact.
/// The column is read-only diagnostic and business data — it enters no index, no claim and no
/// ordering, for the reason the paragraph above gives.
/// </para>
/// <para>
/// <b>Correlation is copied when the producer had one and minted once here when it did not.</b>
/// This seam is the last point at which a single id still covers the whole fan-out: downstream,
/// <c>OutboxMessageFactory.ResolveCorrelation</c> substitutes a fresh id per staging call, so N
/// consumers of one uncorrelated delivery would each start an unrelated chain and none of them
/// would reach back to the delivery that caused all of them.
/// </para>
/// <para>
/// <b>Causation is copied onto the ROW and derived for the SCOPE — this class does both, in the
/// same method.</b> Two different questions, one hop apart, and the answer differs by target:
/// <list type="bullet">
///   <item><see cref="InboxMessage.CausationId"/> is <b>copied</b> from
///         <c>causationid</c> header. The row records who caused the
///         <i>message</i>, which the producer assigned; deriving it here would overwrite the
///         producer's link and lose the hop that crossed the wire.</item>
///   <item>The <see cref="IExecutionContext"/> built below is <b>derived</b> —
///         <see cref="MessageEnvelope.Id"/>, never the envelope's own causation. Everything
///         recorded in this scope was caused by delivering <i>this</i> message, so the cause is
///         its identity; copying would name the grandparent, one hop too far up.</item>
/// </list>
/// <c>InboxProcessor</c> then derives once more, one step later, putting the row's
/// <c>MessageId</c> into the scope it builds for the handler. Three statements about causation on
/// one path, and reading any of them as the rule for all three is the copy-versus-derive confusion
/// that left <c>CausationId</c> null on every row of every path without a red test.
/// </para>
/// <para>
/// <b>One execution scope per envelope</b>, created here rather than expected from the caller. A
/// consume loop holding this singleton cannot then share a <c>DbContext</c> across messages by
/// accident, and a tenant-aware <see cref="IExecutionScopeFactory"/> gets the envelope's tenant
/// before <see cref="IInboxWriter"/> is resolved — which is what puts the row in the right
/// database. One scope for the whole fan-out, not one per consumer: every row carries the same
/// payload and the same tenant, so there is nothing to isolate between them.
/// </para>
/// </remarks>
internal sealed class EnvelopeReceiver(
    IntegrationEventRegistry contracts,
    MessageHandlerRegistry handlers,
    IExecutionScopeFactory executionScopeFactory,
    TimeProvider timeProvider,
    InboxMetrics metrics,
    ILogger<EnvelopeReceiver> logger)
    : IEnvelopeReceiver
{
    /// <inheritdoc />
    public async ValueTask<EnvelopeReceiveResult> ReceiveAsync(
        MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // TryResolveLocalType, not ResolveLocalType. The registry's own docs draw the line: a miss
        // in this direction is DATA off the wire, not a programming error, so the caller supplies
        // the classification. Ours is InboxPayloadException — permanent, and with no row written
        // there is nothing to dead-letter but the broker message itself.
        if (!contracts.TryResolveLocalType(envelope.Type, out var eventType))
        {
            throw new InboxPayloadException(
                $"Contract name '{envelope.Type}' (from '{envelope.Source}') resolves to " +
                "no local type, so this message cannot be recorded for any consumer. Add " +
                "events.Consumes<TEvent>() to the module's composition root, with " +
                "a [MessageContract] whose wire type is that name on the type. This is permanent " +
                "until the process is redeployed — dead-letter the message rather than nacking it " +
                "for retry.");
        }

        // The inbox row stores the body as JSON text and the id as a GUID (#186 boundary): an
        // envelope that is neither cannot be recorded, and no redelivery will change that.
        if (!SystemTextJsonMessageSerializer.IsJson(envelope.DataContentType))
        {
            throw new InboxPayloadException(
                $"Message '{envelope.Id}' ('{envelope.Type}') carries content type " +
                $"'{envelope.DataContentType}'. The inbox records bodies as application/json text " +
                "only, so this message cannot be recorded for any consumer.");
        }

        if (!Guid.TryParse(envelope.Id, out var envelopeId))
        {
            throw new InboxPayloadException(
                $"Message id '{envelope.Id}' ('{envelope.Type}') is not a GUID. The inbox keys its " +
                "deduplication on a GUID message id, so this message cannot be recorded.");
        }

        var tenantId = envelope.Headers.TryGetValue(MessageHeaders.TenantId, out var tenant)
            ? tenant
            : null;

        var consumers = handlers.GetHandlers(eventType);

        if (consumers.Count == 0)
        {
            // A resolvable name with nothing to consume it. Legitimate — a service may understand a
            // contract it does not handle — so zero rows is the correct outcome and nothing throws.
            // But zero rows is otherwise indistinguishable from a healthy delivery, and silent
            // success is forbidden in this module, so it is said out loud and counted. The
            // composition this catches is real: Consumes<T>() declared, AddMessageHandler<,>()
            // forgotten.
            InboxIngestionLogs.NoConsumer(
                logger, envelope.Type, envelope.Source, eventType.FullName!);
            metrics.RecordUnconsumed(envelope.Type);

            return EnvelopeReceiveResult.From(rowsAdded: 0, duplicates: 0);
        }

        var messageId = MessageId.From(envelopeId);
        var payload = Encoding.UTF8.GetString(envelope.Body.Span);
        var eventTypeName = eventType.AssemblyQualifiedName!;
        var receivedAtUtc = timeProvider.GetUtcNow();
        var causationId = TryReadGuid(envelope, MessageHeaders.CausationId) is { } causation
            ? CausationId.From(causation)
            : null;

        // Copied when the producer had one. Minted ONCE here when it did not — and this seam is
        // the last place where minting still covers the whole fan-out. Downstream,
        // OutboxMessageFactory.ResolveCorrelation substitutes a fresh id per staging call, so N
        // consumers of one uncorrelated delivery would land on N unrelated chains, none of them
        // reaching back to the delivery that caused all of them. One id assigned here puts every
        // row, the scope, and everything the handlers go on to stage on the same chain.
        var correlationId = TryReadGuid(envelope, MessageHeaders.CorrelationId) is { } correlation
            ? CorrelationId.From(correlation)
            : CorrelationId.New();

        var context = new MessageCtx
        {
            TenantId = tenantId,

            // Correlation identifies the chain and never advances — copied, or the one minted
            // just above. Causation is DERIVED from the envelope's own MessageId: everything
            // recorded in this scope was caused by delivering THIS message, so the cause is its
            // identity. Copying the envelope's causationid header would name the grandparent, one hop too far
            // up.
            CorrelationId = correlationId.Value.ToString(),
            CausationId = envelopeId.ToString(),
        };

        await using var scope = await executionScopeFactory
            .CreateScopeAsync(context, ct)
            .ConfigureAwait(false);

        var writer = ResolveWriter(scope.ServiceProvider);

        var added = 0;
        var duplicates = 0;

        foreach (var consumer in consumers)
        {
            // A fresh row per consumer: each carries its own RowId and is tracked separately, and
            // the pair (MessageId, ConsumerType) is what the unique index deduplicates on.
            var row = new InboxMessage
            {
                MessageId = messageId,
                ConsumerType = consumer.ConsumerType,
                TenantId = tenantId,
                EventType = eventTypeName,
                Payload = payload,
                Status = InboxMessageStatus.Received,
                ReceivedAtUtc = receivedAtUtc,

                // The producer's business clock, carried through. Never the claim's sort key —
                // that is ReceivedAtUtc, one line above, and the two are seeded in opposite
                // directions by the tests precisely so a swap cannot pass.
                OccurredOnUtc = envelope.Time,

                CorrelationId = correlationId,
                CausationId = causationId,
            };

            var result = await writer.AddAsync(row, ct).ConfigureAwait(false);
            metrics.Record(result, consumer.ConsumerType);

            if (result is InboxWriteResult.AlreadyPresent)
            {
                InboxIngestionLogs.Deduplicated(logger, envelopeId, consumer.ConsumerType);
                duplicates++;

                // `continue`, never `return`. A redelivery absorbed for one consumer must not cost
                // the consumers after it their rows — that is the defect ADR-MSG-017 §6 exists to
                // prevent, and property 4 of the retired InboxRedeliveryTests. A partial
                // redelivery would otherwise become permanent loss for consumers 3..N.
                continue;
            }

            // Neither branch may GUESS. Counting an unrecognised result as Added would make
            // RowsAdded and ConsumersMatched quietly wrong while InboxMetrics.Record, which has
            // the same two cases, counted nothing at all — the two would disagree about the same
            // write. A third InboxWriteResult value is this module changing an enum and missing a
            // call site, so it is loud here and loud there, and correct in neither by accident.
            if (result is not InboxWriteResult.Added)
            {
                throw new InvalidOperationException(
                    $"Unhandled {nameof(InboxWriteResult)} '{result}' from " +
                    $"{nameof(IInboxWriter)}.{nameof(IInboxWriter.AddAsync)}. Every value must be " +
                    $"classified here and in {nameof(InboxMetrics)}.{nameof(InboxMetrics.Record)}, " +
                    "which carry the same two cases and must not drift apart.");
            }

            InboxIngestionLogs.Added(logger, envelopeId, consumer.ConsumerType);
            added++;
        }

        return EnvelopeReceiveResult.From(added, duplicates);
    }

    private static Guid? TryReadGuid(MessageEnvelope envelope, string header)
        => envelope.Headers.TryGetValue(header, out var value) && Guid.TryParse(value, out var id)
            ? id
            : null;

    /// <summary>Resolves the writer, converting a MISSING registration into a typed fault.</summary>
    /// <remarks>
    /// <para>
    /// <c>GetService</c> and a null test, deliberately, and <b>not</b> <c>GetRequiredService</c>
    /// inside a <c>catch (InvalidOperationException)</c>. The two read alike and classify very
    /// differently: <see cref="IInboxWriter"/> is registered as a factory that resolves
    /// <c>EfInboxStore&lt;TContext&gt;</c>, which activates the consumer's <c>DbContext</c>, so a
    /// <c>catch</c> around the resolution spans that entire graph rather than the registration
    /// lookup it appears to guard.
    /// </para>
    /// <para>
    /// <b>The case that matters is the one this seam exists for.</b> A tenant-aware
    /// <see cref="IExecutionScopeFactory"/> resolves a per-tenant connection while the writer is
    /// being activated; an envelope naming an unknown or de-provisioned tenant makes that throw
    /// <see cref="InvalidOperationException"/>. Caught, it would be reported as a missing
    /// registration that is in fact present, told "retrying will not help", and — since
    /// <see cref="InboxConfigurationException"/> instructs a provider to stop consuming — one
    /// tenant's bad row would halt ingestion for every tenant.
    /// </para>
    /// <para>
    /// <c>GetService</c> separates the two structurally rather than by inspecting an exception:
    /// it returns <see langword="null"/> only when nothing is registered, and throws when
    /// something is registered and cannot be activated. The first is a deployment defect and is
    /// the only configuration fault here. The second propagates unchanged, so a provider nacks
    /// one message — the correct verdict for a fault a redelivery, or a different tenant, may
    /// simply not reproduce.
    /// </para>
    /// </remarks>
    private static IInboxWriter ResolveWriter(IServiceProvider serviceProvider)
        => serviceProvider.GetService<IInboxWriter>()
            ?? throw new InboxConfigurationException(
                $"{nameof(IInboxWriter)} is not registered, so an arriving message cannot be " +
                "recorded at all. Call AddEfCoreOutbox<TContext>() on the MessagingBuilder — it " +
                "wires the inbox as well as the outbox. Retrying will not help.");
}
