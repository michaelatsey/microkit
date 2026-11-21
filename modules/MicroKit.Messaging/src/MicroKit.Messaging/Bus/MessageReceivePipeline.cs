namespace MicroKit.Messaging.Bus;

using System.Text;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Processing;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Stores;
using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

/// <summary>
/// The receive pipeline: from one message to every handler of its contract, with a verdict for the
/// transport. The one pipeline for a transport delivery, an <see cref="IEnvelopeReceiver"/> delivery
/// and a store's retry (#188 D6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stages.</b> The contract, the identifier and the body are resolved once, before any scope:
/// an unknown contract, an identifier that is not a GUID and an undeserializable body are
/// permanent, so such a message is dead-lettered on its first delivery and reaches no handler.
/// Then, for each handler of the contract, in its own scope: the scope is created through
/// <see cref="IExecutionScopeFactory"/> and its execution context filled from the envelope —
/// tenant from <c>tenantid</c>, correlation from <c>correlationid</c>, causation set to the
/// incoming message id — then the <see cref="IMessageScopeInitializer"/> instances run in
/// registration order, then the <see cref="IMessageReceiveMiddleware"/> chain, then the handler,
/// resolved last.
/// </para>
/// <para>
/// <b>With a store, each handler is guarded by its inbox row</b> (<see cref="IInboxGuardStore"/>),
/// in the store the handler resolves to (D3):
/// <list type="bullet">
///   <item>a row already there — whatever its state — skips the handler: a redelivery never starts
///         a second attempt;</item>
///   <item>otherwise the handler runs as one operation of the store's execution strategy, each
///         attempt in a <b>fresh scope</b> with its own transaction: the row, enlisted
///         <c>Processed</c>, the handler's own saves, its publications and the engine's save commit
///         together. An attempt whose commit outcome is unknown is verified by reading the row,
///         never replayed blindly;</item>
///   <item>a concurrent delivery that commits the row first makes this one a duplicate: rolled
///         back, and a success;</item>
///   <item>a handler that fails rolls back everything it did; its row is then written
///         <c>Failed</c> in a scope of its own, and only once that row is committed is the
///         delivery acknowledged. The store's retry loop owns it from there.</item>
/// </list>
/// Exactly-once covers what the handler writes through the store's <c>DbContext</c>; anything else
/// it does is at-least-once, because a replayed attempt reruns the handler.
/// </para>
/// <para>
/// <b>Verdicts.</b> <see cref="PermanentMessageException"/> and <see cref="InboxPayloadException"/>
/// are permanent; <see cref="InboxDependencyUnavailableException"/> is dependency-unavailable; any
/// other exception is transient. Several handlers yield the most redeliverable of their verdicts.
/// A guarded handler whose failure is recorded yields success: the store retries it, not the
/// transport.
/// </para>
/// <para>
/// <b>Shutdown is not a failure.</b> An <see cref="OperationCanceledException"/> while the host
/// token is cancelled, or an <see cref="ObjectDisposedException"/> from a disposed scope or
/// provider (or raised while the host token is cancelled), stops the delivery at once with
/// <see cref="DeliveryVerdict.Unsettled"/>: never classified, never counted.
/// </para>
/// </remarks>
internal sealed class MessageReceivePipeline(
    IntegrationEventRegistry contracts,
    MessageHandlerRegistry handlers,
    LocalConsumerRegistry localConsumers,
    IMessageSerializer serializer,
    IExecutionScopeFactory scopeFactory,
    MessageStoreResolver stores,
    InboxRetryPolicy retryPolicy,
    InboxMetrics metrics,
    ILogger<MessageReceivePipeline> logger)
{
    /// <summary>Delivers one envelope to every handler of its contract.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="ct">The host's token; its cancellation means shutdown.</param>
    /// <returns>What the transport must do with the message.</returns>
    public async ValueTask<DeliveryOutcome> DeliverAsync(
        MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Everything before the per-handler stage runs once, and a failure here is never a
        // handler's: shutdown leaves the message unsettled (decision 8), anything else makes it
        // permanently undeliverable (decision 7) — a malformed envelope is dead-lettered, never
        // allowed to escape and take the consume loop with it.
        Prepared prepared;
        try
        {
            var result = Prepare(envelope, ct);
            if (result.Outcome is { } early)
            {
                return early;
            }

            prepared = result.Prepared!.Value;
        }
        catch (Exception ex) when (IsShutdown(ex, ct))
        {
            return new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            return DeliveryOutcome.Permanent(
                $"Message '{envelope.Id}' ('{envelope.Type}') failed before any handler: {ex.Message}",
                ex);
        }

        return await DeliverPreparedAsync(prepared, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delivers one local message (#188 D7) — an outbox row routed to the local endpoint, or an entry
    /// of the in-memory queue of a host with no store — to one local consumer or to every local
    /// consumer of its type, as a transport delivery reaches its handlers: each guarded by its inbox
    /// row when it has a store.
    /// </summary>
    /// <param name="message">The local row: its type, payload and metadata.</param>
    /// <param name="consumerType">
    /// One consumer to deliver to — an entry of the in-memory queue of a host with no store — or
    /// <see langword="null"/> for every consumer of the type, each guarded by its own inbox row.
    /// </param>
    /// <param name="ct">The host's token; its cancellation means shutdown.</param>
    /// <returns>The verdict, for the outbox loop or the local queue to settle.</returns>
    /// <remarks>
    /// Its context carries no contract — <see cref="MessageContext.ContractName"/> is empty and
    /// <see cref="MessageContext.ContractVersion"/> zero — and its headers are rebuilt from the
    /// row's columns, as the transport dispatcher builds an envelope's.
    /// </remarks>
    public async ValueTask<DeliveryOutcome> DeliverLocalAsync(
        OutboxMessage message, string? consumerType = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Prepared prepared;
        try
        {
            var result = PrepareLocal(message, consumerType, ct);
            if (result.Outcome is { } early)
            {
                return early;
            }

            prepared = result.Prepared!.Value;
        }
        catch (Exception ex) when (IsShutdown(ex, ct))
        {
            return new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            return DeliveryOutcome.Permanent(
                $"Local message '{message.Id}' ('{message.EventType}') failed before any consumer: {ex.Message}",
                ex);
        }

        return await DeliverPreparedAsync(prepared, ct).ConfigureAwait(false);
    }

    /// <summary>The per-handler stage, shared by a transport delivery and a local one.</summary>
    private async ValueTask<DeliveryOutcome> DeliverPreparedAsync(Prepared prepared, CancellationToken ct)
    {
        var outcome = DeliveryOutcome.Success;

        foreach (var entry in prepared.Entries)
        {
            var store = stores.ForHandler(entry.HandlerType);
            var handlerOutcome = store is null
                ? await DeliverToHandlerAsync(entry, prepared, ct).ConfigureAwait(false)
                : await DeliverGuardedAsync(entry, prepared, store, ct).ConfigureAwait(false);

            if (handlerOutcome.Verdict is DeliveryVerdict.Unsettled)
            {
                return handlerOutcome;
            }

            outcome = DeliveryOutcome.MostRedeliverable(outcome, handlerOutcome);
        }

        return outcome;
    }

    /// <summary>
    /// Runs one retry of a handler from its claimed inbox row, guarded like a delivery: one operation
    /// of the store's execution strategy, a fresh scope and transaction per attempt, the row marked
    /// <c>Processed</c> in the handler's transaction, conditioned on the claim.
    /// </summary>
    /// <param name="row">The claimed row.</param>
    /// <param name="entry">The handler.</param>
    /// <param name="message">The message, read back from the row.</param>
    /// <param name="context">The handler's context, rebuilt from the row.</param>
    /// <param name="claimToken">The token of the claim that owns the row.</param>
    /// <param name="store">The store the row lives in.</param>
    /// <param name="ct">The host's token; its cancellation means shutdown.</param>
    /// <returns>What happened to the row; the caller settles failures through the claim.</returns>
    /// <remarks>
    /// The row exists by construction, so nothing here is a duplicate (C1): a lost claim — the
    /// conditioned write matching no row — is <see cref="RetryResult.LeaseLost"/>, and every other
    /// exception is the handler's failure, a business unique violation included.
    /// </remarks>
    public async ValueTask<RetryOutcome> RetryAsync(
        InboxMessage row,
        MessageHandlerRegistry.HandlerEntry entry,
        object message,
        MessageContext context,
        Guid claimToken,
        MessageStoreKey store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);

        var key = new InboxMessageKey(row.MessageId, row.ConsumerType);
        var attempt = new GuardedAttempt(entry, message, context, ExecutionContextOf(context), store, key)
        {
            ClaimToken = claimToken,
        };

        IInboxGuardStore? guard = null;
        try
        {
            var outer = await OpenScopeAsync(attempt.ExecutionContext, store, context, ct).ConfigureAwait(false);
            await using (outer.ConfigureAwait(false))
            {
                guard = ResolveGuard(outer.ServiceProvider, store) ?? throw Unregistered(store);

                var result = await RunGuardedAsync(guard, attempt, ct).ConfigureAwait(false);
                if (result is AttemptResult.LeaseLost)
                {
                    InboxGuardLogs.LeaseLost(logger, null, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
                    return new RetryOutcome(RetryResult.LeaseLost);
                }

                return new RetryOutcome(RetryResult.Processed);
            }
        }
        catch (Exception ex) when (IsShutdown(ex, ct))
        {
            return new RetryOutcome(RetryResult.Unsettled, Error: ex);
        }
        catch (Exception ex) when (attempt.Committed)
        {
            InboxGuardLogs.FaultAfterCommit(logger, ex, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
            return new RetryOutcome(RetryResult.Processed);
        }
        catch (InboxConfigurationException)
        {
            // A store without its guard: the retry loop settles its batch and stops.
            throw;
        }
        catch (Exception ex) when (guard is not null && guard.IsLeaseLost(ex))
        {
            InboxGuardLogs.LeaseLost(logger, ex, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
            return new RetryOutcome(RetryResult.LeaseLost, Error: ex);
        }
        catch (Exception ex)
        {
            return new RetryOutcome(RetryResult.Failed, Classify(ex), ex);
        }
    }

    /// <summary>
    /// Resolves the contract, the identifier, the body, the handlers and the context — once, before
    /// any scope. Returns either an early outcome (permanent, or success when no handler is
    /// registered) or what the per-handler stage needs.
    /// </summary>
    /// <remarks>
    /// May throw; the caller classifies anything thrown here as shutdown or permanent.
    /// </remarks>
    private (DeliveryOutcome? Outcome, Prepared? Prepared) Prepare(
        MessageEnvelope envelope, CancellationToken ct)
    {
        if (!contracts.TryResolveLocalType(envelope.Type, out var eventType))
        {
            return (DeliveryOutcome.Permanent(
                $"Wire type '{envelope.Type}' resolves to no local type in this process. Register " +
                "a type carrying the matching [MessageContract]."), null);
        }

        if (!Guid.TryParse(envelope.Id, out var id))
        {
            return (DeliveryOutcome.Permanent($"Message id '{envelope.Id}' is not a GUID."), null);
        }

        object message;
        try
        {
            message = serializer.Deserialize(envelope.Body, eventType) as IIntegrationEvent
                ?? throw new InboxPayloadException(
                    $"The body deserialized to null or to a type that is not '{eventType.FullName}'.");
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            // Any exception a serializer raises outside shutdown — an OperationCanceledException
            // included — is a body this process can never read.
            return (DeliveryOutcome.Permanent(
                $"The body of '{envelope.Type}' cannot be deserialized into '{eventType.FullName}'.",
                ex), null);
        }

        var entries = handlers.GetHandlers(eventType);
        if (entries.Count == 0)
        {
            BusLogs.NoHandler(logger, envelope.Id, envelope.Type, envelope.Source, eventType.FullName!);
            metrics.RecordUnconsumed(envelope.Type);
            return (DeliveryOutcome.Success, null);
        }

        var context = BuildContext(envelope, eventType, MessageId.From(id));
        var incoming = new Incoming(
            envelope.Id, envelope.Type, envelope.Source, envelope.Time, envelope.Headers,
            () => Encoding.UTF8.GetString(envelope.Body.Span));
        return (null, new Prepared(message, context, entries, eventType, incoming));
    }

    /// <summary>
    /// <see cref="Prepare"/> for a local row: its type from <see cref="OutboxMessage.EventType"/>,
    /// its consumers from the local registry, its context from its columns.
    /// </summary>
    private (DeliveryOutcome? Outcome, Prepared? Prepared) PrepareLocal(
        OutboxMessage row, string? consumerType, CancellationToken ct)
    {
        var id = row.Id.Value.ToString();
        var eventType = RowEventTypes.Resolve(row.EventType);
        if (eventType is null)
        {
            return (DeliveryOutcome.Permanent(
                $"Local message type '{row.EventType}' resolves to no type in this process."), null);
        }

        object message;
        try
        {
            message = serializer.Deserialize(Encoding.UTF8.GetBytes(row.Payload), eventType)
                ?? throw new InboxPayloadException($"The payload deserialized to null, not '{eventType.FullName}'.");
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            return (DeliveryOutcome.Permanent(
                $"The payload of local message '{id}' cannot be deserialized into '{eventType.FullName}'.",
                ex), null);
        }

        var source = row.Source ?? string.Empty;
        IReadOnlyList<MessageHandlerRegistry.HandlerEntry> entries = consumerType is null
            ? localConsumers.GetConsumers(eventType)
            : localConsumers.TryGetConsumer(consumerType, eventType, out var only) ? [only] : [];
        if (entries.Count == 0)
        {
            BusLogs.NoHandler(logger, id, eventType.FullName!, source, eventType.FullName!);
            metrics.RecordUnconsumed(eventType.FullName!);
            return (DeliveryOutcome.Success, null);
        }

        var headers = LocalHeaders(row);
        var context = new MessageContext
        {
            MessageId = row.Id,
            ContractName = string.Empty,
            ContractVersion = 0,
            Source = source,
            OccurredOnUtc = row.OccurredOnUtc,
            TenantId = row.TenantId,
            CorrelationId = headers.GetValueOrDefault(MessageHeaders.CorrelationId) ?? Guid.NewGuid().ToString(),
            CausationId = row.CausationId?.Value.ToString(),
            Headers = headers,
        };

        var incoming = new Incoming(id, eventType.FullName!, source, row.OccurredOnUtc, headers, () => row.Payload);
        return (null, new Prepared(message, context, entries, eventType, incoming));
    }

    /// <summary>A local row's headers: its own, then the reserved ones from its columns.</summary>
    private static Dictionary<string, string> LocalHeaders(OutboxMessage row)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (row.Headers is { } extra)
        {
            foreach (var (name, value) in extra)
            {
                if (!MessageHeaders.IsReserved(name))
                {
                    headers[name] = value;
                }
            }
        }

        if (row.CorrelationId is not null)
        {
            headers[MessageHeaders.CorrelationId] = row.CorrelationId.Value.ToString();
        }

        if (row.TenantId is not null)
        {
            headers[MessageHeaders.TenantId] = row.TenantId;
        }

        if (row.CausationId is not null)
        {
            headers[MessageHeaders.CausationId] = row.CausationId.Value.ToString();
        }

        return headers;
    }

    /// <summary>What the per-handler stage needs, resolved once per delivery.</summary>
    private readonly record struct Prepared(
        object Message,
        MessageContext Context,
        IReadOnlyList<MessageHandlerRegistry.HandlerEntry> Entries,
        Type EventType,
        Incoming Incoming);

    /// <summary>The arriving message, whatever carried it: what logs name and an inbox row records.</summary>
    /// <param name="Id">The message id, as carried.</param>
    /// <param name="Type">The wire type, or a local message's type name.</param>
    /// <param name="Source">The emitting service.</param>
    /// <param name="Time">When it occurred.</param>
    /// <param name="Headers">Its headers, reserved ones included.</param>
    /// <param name="Payload">Its body as text, decoded only when a row is written.</param>
    private sealed record Incoming(
        string Id,
        string Type,
        string Source,
        DateTimeOffset Time,
        IReadOnlyDictionary<string, string> Headers,
        Func<string> Payload);

    /// <summary>One handler, with no store: the #186 path, unchanged.</summary>
    private async ValueTask<DeliveryOutcome> DeliverToHandlerAsync(
        MessageHandlerRegistry.HandlerEntry entry,
        Prepared prepared,
        CancellationToken ct)
    {
        var (message, context, envelope) = (prepared.Message, prepared.Context, prepared.Incoming);
        try
        {
            var executionContext = ExecutionContextOf(context);

            var scope = await OpenScopeAsync(executionContext, store: null, context, ct).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                await RunHandlerAsync(scope.ServiceProvider, entry, message, context, ct).ConfigureAwait(false);
            }

            return DeliveryOutcome.Success;
        }
        catch (Exception ex) when (IsShutdown(ex, ct))
        {
            BusLogs.Unsettled(logger, ex, envelope.Id, envelope.Type, entry.HandlerType.FullName!);
            return new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            var verdict = Classify(ex);
            BusLogs.HandlerFailed(
                logger, ex, entry.HandlerType.FullName!, envelope.Id, envelope.Type, verdict);
            return new DeliveryOutcome(verdict, ex, ex.Message);
        }
    }

    /// <summary>One handler of a new delivery, guarded by its inbox row in <paramref name="store"/>.</summary>
    private async ValueTask<DeliveryOutcome> DeliverGuardedAsync(
        MessageHandlerRegistry.HandlerEntry entry,
        Prepared prepared,
        MessageStoreKey store,
        CancellationToken ct)
    {
        var context = prepared.Context;
        var envelope = prepared.Incoming;
        var key = new InboxMessageKey(context.MessageId, entry.ConsumerType);
        var rows = new RowFactory(prepared, entry.ConsumerType, retryPolicy.Clock);
        var attempt = new GuardedAttempt(entry, prepared.Message, context, ExecutionContextOf(context), store, key)
        {
            NewRow = rows,
        };

        try
        {
            var outer = await OpenScopeAsync(attempt.ExecutionContext, store, context, ct).ConfigureAwait(false);
            await using (outer.ConfigureAwait(false))
            {
                var guard = ResolveGuard(outer.ServiceProvider, store);
                if (guard is null)
                {
                    InboxGuardLogs.GuardUnregistered(logger, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
                    return new DeliveryOutcome(DeliveryVerdict.Transient, Unregistered(store));
                }

                // D6a/b: a row in any state means another delivery owns this pair.
                if (await guard.FindAsync(key, ct).ConfigureAwait(false) is { } existing)
                {
                    InboxGuardLogs.Skipped(logger, entry.HandlerType.FullName!, key.MessageId.Value, store.Value, existing.Status);
                    metrics.RecordDeduplicated(entry.ConsumerType);
                    return DeliveryOutcome.Success;
                }

                try
                {
                    await RunGuardedAsync(guard, attempt, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!IsShutdown(ex, ct))
                {
                    if (attempt.Committed)
                    {
                        InboxGuardLogs.FaultAfterCommit(logger, ex, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
                        metrics.RecordAdded(entry.ConsumerType);
                        return DeliveryOutcome.Success;
                    }

                    // An outage is not a race: no read, which would fail too and hide the verdict.
                    var found = Classify(ex) is DeliveryVerdict.DependencyUnavailable
                        ? null
                        : await guard.FindAsync(key, ct).ConfigureAwait(false);

                    // This attempt's own row: the commit landed, then the fault — with no retrying
                    // strategy nothing verified it.
                    if (found is { } own && own.RowId == attempt.LastRowId)
                    {
                        InboxGuardLogs.FaultAfterCommit(logger, ex, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
                        metrics.RecordAdded(entry.ConsumerType);
                        return DeliveryOutcome.Success;
                    }

                    // D6d, a new delivery only (C1): the row is there now, so a concurrent delivery
                    // committed it first and this attempt's save hit the unique index.
                    if (found is not null)
                    {
                        InboxGuardLogs.ConcurrentDuplicate(logger, entry.HandlerType.FullName!, key.MessageId.Value, store.Value);
                        metrics.RecordDeduplicated(entry.ConsumerType);
                        return DeliveryOutcome.Success;
                    }

                    return await RecordFailureAsync(attempt, rows, ex, ct).ConfigureAwait(false);
                }

                metrics.RecordAdded(entry.ConsumerType);
                return DeliveryOutcome.Success;
            }
        }
        catch (Exception ex) when (IsShutdown(ex, ct))
        {
            BusLogs.Unsettled(logger, ex, envelope.Id, envelope.Type, entry.HandlerType.FullName!);
            return new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            // Outside any attempt — the scope, the guard's read: nothing was recorded, so the
            // transport redelivers.
            BusLogs.HandlerFailed(
                logger, ex, entry.HandlerType.FullName!, envelope.Id, envelope.Type, DeliveryVerdict.Transient);
            return new DeliveryOutcome(DeliveryVerdict.Transient, ex, ex.Message);
        }
    }

    /// <summary>
    /// Runs the attempts of one guarded handler through the store's execution strategy. The commit
    /// outcome of a failed attempt is verified by reading the row: committed means
    /// <c>Processed</c>.
    /// </summary>
    private async ValueTask<AttemptResult> RunGuardedAsync(
        IInboxGuardStore guard, GuardedAttempt attempt, CancellationToken ct)
        => await guard.ExecuteAsync(
            token => AttemptAsync(attempt, token),
            async token => await guard.FindAsync(attempt.Key, token).ConfigureAwait(false)
                is { Status: InboxMessageStatus.Processed },
            AttemptResult.Committed,
            ct).ConfigureAwait(false);

    /// <summary>
    /// One attempt, from a clean state: a fresh scope with its initializers run, its own transaction,
    /// the row, then the middlewares and the handler, then the engine's save and the commit.
    /// </summary>
    /// <remarks>
    /// The initializers run before the transaction is begun because they configure the scope the
    /// store's <c>DbContext</c> is built from — a tenant's connection, say — and that context begins
    /// the transaction. Everything that writes — the middlewares and the handler — runs inside it.
    /// </remarks>
    /// <remarks>
    /// The store holder and the execution context are written on <b>this</b> scope (C2): a replayed
    /// attempt is a new scope, and the handler's publications must land in the delivery's store on
    /// every attempt, not only the first.
    /// </remarks>
    private async ValueTask<AttemptResult> AttemptAsync(GuardedAttempt attempt, CancellationToken ct)
    {
        var scope = await OpenScopeAsync(attempt.ExecutionContext, attempt.Store, attempt.Context, ct).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            var guard = ResolveGuard(services, attempt.Store) ?? throw Unregistered(attempt.Store);
            await guard.BeginAsync(ct).ConfigureAwait(false);

            if (attempt.ClaimToken is { } token)
            {
                if (!await guard.MarkClaimedProcessedAsync(attempt.Key, token, ct).ConfigureAwait(false))
                {
                    return AttemptResult.LeaseLost;
                }
            }
            else
            {
                var row = attempt.NewRow!.Create(InboxMessageStatus.Processed);
                attempt.LastRowId = row.RowId;
                await guard.EnlistProcessedAsync(row, ct).ConfigureAwait(false);
            }

            await RunHandlerAsync(services, attempt.Entry, attempt.Message, attempt.Context, ct)
                .ConfigureAwait(false);

            await guard.CommitAsync(ct).ConfigureAwait(false);
            attempt.Committed = true;
        }

        return AttemptResult.Committed;
    }

    /// <summary>
    /// D6e: the handler of a new delivery failed and its transaction rolled back. Writes its row
    /// <c>Failed</c> in a scope of its own; only once that row is committed is the delivery a
    /// success for the transport.
    /// </summary>
    private async ValueTask<DeliveryOutcome> RecordFailureAsync(
        GuardedAttempt attempt, RowFactory rows, Exception failure, CancellationToken ct)
    {
        var handlerType = attempt.Entry.HandlerType.FullName!;
        var messageId = attempt.Key.MessageId.Value;
        var verdict = Classify(failure);

        if (verdict is DeliveryVerdict.DependencyUnavailable)
        {
            // No row: the dependency is down for every message, so the transport pauses and
            // redelivers this one rather than the store counting an attempt it never really had.
            BusLogs.HandlerFailed(logger, failure, handlerType, messageId.ToString(), attempt.Context.ContractName, verdict);
            return new DeliveryOutcome(verdict, failure, failure.Message);
        }

        // A permanent verdict counts no attempt: no number of attempts would change it.
        var permanent = verdict is DeliveryVerdict.Permanent;
        var attempts = permanent ? 0 : 1;
        var deadLettered = permanent || retryPolicy.IsExhausted(attempts);
        var row = rows.Create(InboxMessageStatus.Failed);
        row.RetryCount = attempts;
        row.DeadLettered = deadLettered;
        row.ErrorMessage = retryPolicy.Truncate(failure.Message);
        row.NextRetryAtUtc = deadLettered ? null : retryPolicy.NextAttemptAt(attempts);
        row.ProcessedAtUtc = deadLettered ? retryPolicy.Clock.GetUtcNow() : null;

        try
        {
            var scope = await OpenScopeAsync(attempt.ExecutionContext, attempt.Store, attempt.Context, ct).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                var guard = ResolveGuard(scope.ServiceProvider, attempt.Store) ?? throw Unregistered(attempt.Store);
                if (!await guard.TryInsertFailedAsync(row, ct).ConfigureAwait(false))
                {
                    // Another delivery owns the row now — Processed or Failed: acknowledged.
                    InboxGuardLogs.FailureAlreadyOwned(logger, failure, handlerType, messageId, attempt.Store.Value);
                    metrics.RecordDeduplicated(attempt.Entry.ConsumerType);
                    return DeliveryOutcome.Success;
                }
            }
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            InboxGuardLogs.FailedRowUnwritable(logger, ex, handlerType, messageId, attempt.Store.Value);
            return new DeliveryOutcome(DeliveryVerdict.Transient, ex, ex.Message);
        }

        if (deadLettered)
        {
            InboxGuardLogs.DeadLettered(logger, failure, handlerType, messageId, attempt.Store.Value, attempts);
        }
        else
        {
            InboxGuardLogs.FailedRowWritten(
                logger, failure, handlerType, messageId, attempt.Store.Value, attempts, row.NextRetryAtUtc!.Value);
        }

        metrics.RecordAdded(attempt.Entry.ConsumerType);
        return DeliveryOutcome.Success;
    }

    /// <summary>
    /// Opens a message scope ready for anything to be resolved from it: its execution context and,
    /// for a guarded handler, its store written, then the <see cref="IMessageScopeInitializer"/>
    /// instances run — before any service of the scope, the store's <c>DbContext</c> included, is
    /// built. A tenant-aware context reads what the initializers set.
    /// </summary>
    private async ValueTask<IExecutionScope> OpenScopeAsync(
        MessageCtx executionContext, MessageStoreKey? store, MessageContext context, CancellationToken ct)
    {
        var scope = await scopeFactory.CreateScopeAsync(executionContext, ct).ConfigureAwait(false);
        try
        {
            var services = scope.ServiceProvider;
            PrepareScope(services, executionContext, store);
            foreach (var initializer in services.GetServices<IMessageScopeInitializer>())
            {
                await initializer.InitializeAsync(context, services, ct).ConfigureAwait(false);
            }

            return scope;
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Fills a message scope: its execution context and, for a guarded handler, its store.</summary>
    private static void PrepareScope(IServiceProvider services, MessageCtx executionContext, MessageStoreKey? store)
    {
        // The single ambient source (decision 1). A scope factory a host substitutes may not fill
        // the holder itself; the pipeline does, before any initializer runs.
        if (services.GetService<ExecutionContextHolder>() is { } holder)
        {
            holder.Context = executionContext;
        }

        // The delivery's store (D3 rule 2): a publication of the handler is enlisted there.
        if (store is not null && services.GetService<DeliveryStoreHolder>() is { } delivery)
        {
            delivery.Store = store;
        }
    }

    /// <summary>The receive middlewares, then the handler, on a scope <see cref="OpenScopeAsync"/> opened.</summary>
    private static async ValueTask RunHandlerAsync(
        IServiceProvider services,
        MessageHandlerRegistry.HandlerEntry entry,
        object message,
        MessageContext context,
        CancellationToken ct)
    {
        var middlewares = services.GetServices<IMessageReceiveMiddleware>().ToArray();
        var receiveContext = new MessageReceiveContext
        {
            Context = context,
            Message = message,
            HandlerType = entry.HandlerType,
            Services = services,
        };

        await RunAsync(0).ConfigureAwait(false);

        ValueTask RunAsync(int index) =>
            index < middlewares.Length
                ? middlewares[index].InvokeAsync(receiveContext, () => RunAsync(index + 1), ct)
                : InvokeHandlerAsync();

        ValueTask InvokeHandlerAsync() => entry.Invoke(services, message, context, ct);
    }

    private static IInboxGuardStore? ResolveGuard(IServiceProvider services, MessageStoreKey store)
        => services.GetKeyedService<IInboxGuardStore>(store);

    private static InboxConfigurationException Unregistered(MessageStoreKey store)
        => new($"Store '{store}' registers no {nameof(IInboxGuardStore)} under its key, so no " +
               "handler of that store can be guarded. Host start rejects this composition.");

    private static MessageCtx ExecutionContextOf(MessageContext context) => new()
    {
        TenantId = context.TenantId,
        CorrelationId = context.CorrelationId,
        CausationId = context.MessageId.ToString(),
    };

    /// <summary>
    /// Whether <paramref name="exception"/> is the host shutting down rather than a failure.
    /// Shared with the in-memory consume loop's safety net.
    /// </summary>
    /// <remarks>
    /// An <see cref="ObjectDisposedException"/> is shutdown when the host token is cancelled, or
    /// when it names the service provider — the scope or container disposed under the delivery.
    /// One raised by a handler's own object (a disposed <c>HttpClient</c>, a context used after
    /// dispose) while the host keeps running is an ordinary failure: classifying it as shutdown
    /// would leave the message unsettled and stop the consume loop of a live host.
    /// </remarks>
    internal static bool IsShutdown(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException => ct.IsCancellationRequested,
        ObjectDisposedException disposed => ct.IsCancellationRequested
            || string.Equals(disposed.ObjectName, nameof(IServiceProvider), StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>The verdict of a handler's exception.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>Permanent, dependency-unavailable, or transient.</returns>
    internal static DeliveryVerdict Classify(Exception exception) => exception switch
    {
        PermanentMessageException or InboxPayloadException => DeliveryVerdict.Permanent,
        InboxDependencyUnavailableException => DeliveryVerdict.DependencyUnavailable,
        _ => DeliveryVerdict.Transient,
    };

    private MessageContext BuildContext(
        MessageEnvelope envelope, Type eventType, MessageId messageId)
    {
        // The registry carries the contract read at registration (#187): no reflection per
        // delivery. Every type it resolves a wire type to is known to it.
        contracts.TryGetContract(eventType, out var contract);
        var headers = envelope.Headers;

        return new MessageContext
        {
            MessageId = messageId,
            ContractName = contract?.Name ?? envelope.Type,
            ContractVersion = contract?.Version ?? 0,
            Source = envelope.Source,
            OccurredOnUtc = envelope.Time,
            TenantId = headers.GetValueOrDefault(MessageHeaders.TenantId),
            // Minted ONCE when absent, so every handler of this delivery — and every message they
            // publish — shares one chain.
            CorrelationId = headers.GetValueOrDefault(MessageHeaders.CorrelationId)
                ?? Guid.NewGuid().ToString(),
            CausationId = headers.GetValueOrDefault(MessageHeaders.CausationId),
            Headers = headers,
        };
    }

    /// <summary>The state of one guarded handler across its attempts.</summary>
    private sealed class GuardedAttempt(
        MessageHandlerRegistry.HandlerEntry entry,
        object message,
        MessageContext context,
        MessageCtx executionContext,
        MessageStoreKey store,
        InboxMessageKey key)
    {
        public MessageHandlerRegistry.HandlerEntry Entry { get; } = entry;

        public object Message { get; } = message;

        public MessageContext Context { get; } = context;

        public MessageCtx ExecutionContext { get; } = executionContext;

        public MessageStoreKey Store { get; } = store;

        public InboxMessageKey Key { get; } = key;

        /// <summary>Gets the claim of a retry; <see langword="null"/> for a new delivery.</summary>
        public Guid? ClaimToken { get; init; }

        /// <summary>Gets the row builder of a new delivery; <see langword="null"/> for a retry.</summary>
        public RowFactory? NewRow { get; init; }

        /// <summary>Gets or sets the row id of the last attempt's enlisted row, to recognise it in the database.</summary>
        public Guid? LastRowId { get; set; }

        /// <summary>Gets or sets whether an attempt committed: what escapes afterwards is not a failure.</summary>
        public bool Committed { get; set; }
    }

    /// <summary>Builds the inbox row of a new delivery, a fresh instance per attempt.</summary>
    private sealed class RowFactory(
        Prepared prepared, string consumerType, TimeProvider clock)
    {
        // Decoded only when a row is written: a skipped redelivery never pays for it.
        private string? _payload;

        public InboxMessage Create(InboxMessageStatus status)
        {
            var context = prepared.Context;
            var now = clock.GetUtcNow();

            return new InboxMessage
            {
                MessageId = context.MessageId,
                ConsumerType = consumerType,
                TenantId = context.TenantId,
                EventType = prepared.EventType.AssemblyQualifiedName!,
                Payload = _payload ??= prepared.Incoming.Payload(),
                Source = prepared.Incoming.Source,
                Headers = prepared.Incoming.Headers.Count == 0
                    ? null
                    : new Dictionary<string, string>(prepared.Incoming.Headers, StringComparer.Ordinal),
                Status = status,
                ReceivedAtUtc = now,
                OccurredOnUtc = prepared.Incoming.Time,
                ProcessedAtUtc = status is InboxMessageStatus.Processed ? now : null,
                // The columns are GUIDs; a correlation that is not one is not recorded (#206).
                CorrelationId = Guid.TryParse(context.CorrelationId, out var correlation)
                    ? CorrelationId.From(correlation)
                    : null,
                CausationId = Guid.TryParse(context.CausationId, out var causation)
                    ? CausationId.From(causation)
                    : null,
            };
        }
    }
}

/// <summary>What one attempt of a guarded handler produced.</summary>
internal enum AttemptResult
{
    /// <summary>The row and the handler's work committed.</summary>
    Committed,

    /// <summary>A retry's claim was lost before the handler ran; nothing ran.</summary>
    LeaseLost,
}

/// <summary>What a retry did with its claimed row.</summary>
internal enum RetryResult
{
    /// <summary>Handled: the row is Processed, committed with the handler's work.</summary>
    Processed,

    /// <summary>The claim was lost: another loop owns the row; nothing is written or counted.</summary>
    LeaseLost,

    /// <summary>The handler failed and rolled back; the caller writes the failure through the claim.</summary>
    Failed,

    /// <summary>The host stopped; the caller releases the row with nothing counted.</summary>
    Unsettled,
}

/// <summary>The outcome of <see cref="MessageReceivePipeline.RetryAsync"/>.</summary>
/// <param name="Result">What happened to the row.</param>
/// <param name="Verdict">The failure's verdict, for <see cref="RetryResult.Failed"/>.</param>
/// <param name="Error">The exception behind it, if any.</param>
internal readonly record struct RetryOutcome(
    RetryResult Result, DeliveryVerdict Verdict = DeliveryVerdict.Succeeded, Exception? Error = null);
