using MicroKit.Messaging.Registry;

using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Topology-agnostic inbox drain engine. Atomically claims a batch of processable
/// <see cref="InboxMessage"/> rows, resolves and invokes the registered handler for each inside
/// its own execution scope, and settles every disposition.
/// </summary>
/// <remarks>
/// <para>
/// A pure drain loop — it never calls <see cref="IInboxWriter.ExistsAsync"/> or
/// <see cref="IInboxWriter.AddAsync"/>, and its narrowed dependency on
/// <see cref="IInboxProcessorStore"/> makes that structural rather than a convention. Ingestion is
/// somebody else's job — a broker adapter, or the receiving seam that turns a
/// <see cref="MessageEnvelope"/> back into one row per registered consumer.
/// </para>
/// <para>
/// ⚠ <b>No such producer ships in this release.</b> The in-process fan-out that used to write those
/// rows was withdrawn with the in-process transport (ADR-MSG-019) and its replacement has not
/// arrived, so this loop is correct and unfed: it claims from an empty table until a host writes
/// rows itself. <c>InboxIngestionValidator</c> fails startup if handlers are registered so the gap
/// cannot be mistaken for an idle queue.
/// </para>
/// <para>
/// <b>Isolation.</b> One <see cref="IExecutionScope"/> per message — never shared across a
/// batch. A <c>DbContext</c> fault on row N cannot corrupt row N+1, and <c>TenantId</c> context
/// is fresh every time.
/// </para>
/// <para>
/// <b>Success settles inside the handler transaction.</b> The processed mark is staged through
/// <see cref="IInboxSettlementStore"/>, resolved from the message's own scope, before the
/// handler runs. The handler's <c>SaveChanges</c> then commits its side effects and the mark
/// together, or neither. Batching that write would widen the replay window from one handler to
/// N, and unlike an outbox there is no downstream inbox to absorb the difference. Failures are
/// batched, because a failed handler rolled back and has nothing to join.
/// </para>
/// <para>
/// <b>Claim before scope.</b> The batch is reserved before any execution scope is created. A
/// tenant-aware <see cref="IExecutionScopeFactory"/> may perform I/O to resolve a per-tenant
/// connection, and spending that on a row another processor already owns is waste.
/// </para>
/// <para>
/// <b>Failure classification.</b> Failures <i>known</i> to be permanent are dead-lettered on
/// first sight rather than after <c>MaxRetries</c> pointless attempts; a failure known to be
/// fatal to the batch abandons it and releases the remainder untouched, instead of failing every
/// row in turn and burning the whole queue's retry budget during an outage. Everything
/// unrecognised stays transient: a library that guesses permanence wrongly loses messages.
/// </para>
/// <para>
/// <b>Post-commit faults.</b> A handler that commits and then throws has done its work: the row
/// is <c>Processed</c> and durable. The exception is reported but does not turn a successful
/// invocation into a retry — the same rule the command pipeline applies.
/// </para>
/// <para>
/// <b>Delivery semantics.</b> <b>Transactionally atomic processing for database-backed
/// handlers</b>: the processed marker and any database side effects written through the scope's
/// <c>DbContext</c> commit together or not at all. This is not exactly-once in general — a
/// handler that calls an external endpoint and then rolls back will call it again on replay, so
/// database effects happen effectively once and external effects at least once. A handler that
/// commits no unit of work at all is detected and reported rather than degrading silently.
/// </para>
/// </remarks>
internal sealed class InboxProcessor : IInboxProcessor
{
    // Batch-scoped by design (ADR-MSG-002 shared-DB cross-tenant reservation): NOT resolved
    // from the per-message IExecutionScope. Per-tenant DB resolution is the deferred
    // PerTenantInboxCoordinator's responsibility, not the processor's.
    private readonly IInboxProcessorStore _store;
    private readonly MessageHandlerRegistry _registry;
    private readonly IMessageSerializer _serializer;
    private readonly IExecutionScopeFactory _executionScopeFactory;
    private readonly InboxProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly ILogger<InboxProcessor> _logger;

    /// <summary>Initializes a new <see cref="InboxProcessor"/>.</summary>
    /// <param name="store">Claim and deferred settlement, batch-scoped.</param>
    /// <param name="registry">Maps a consumer type to its handler and invoker.</param>
    /// <param name="serializer">Deserializes the persisted payload.</param>
    /// <param name="executionScopeFactory">Creates the per-message execution scope.</param>
    /// <param name="options">Batch size, lease, retry policy.</param>
    /// <param name="timeProvider">Clock used for back-off deadlines.</param>
    /// <param name="random">Jitter source for the back-off draw.</param>
    /// <param name="logger">Logger.</param>
    public InboxProcessor(
        IInboxProcessorStore store,
        MessageHandlerRegistry registry,
        IMessageSerializer serializer,
        IExecutionScopeFactory executionScopeFactory,
        InboxProcessorOptions options,
        TimeProvider timeProvider,
        Random random,
        ILogger<InboxProcessor> logger)
    {
        _store = store;
        _registry = registry;
        _serializer = serializer;
        _executionScopeFactory = executionScopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _random = random;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<InboxBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var claim = await _store
            .ClaimBatchAsync(batchSize, _options.LeaseDuration, cancellationToken)
            .ConfigureAwait(false);

        if (claim.Count == 0)
        {
            return InboxBatchResult.Empty;
        }

        InboxProcessorLogs.BatchClaimed(_logger, claim.Count);

        var messages = claim.Messages;

        // Only failures and releases are buffered. Successes settled themselves inside the
        // handler transaction, which is the whole point of IInboxSettlementStore.
        var deferred = new List<InboxOutcome>(capacity: 8);
        var abortReason = InboxBatchAbortReason.None;
        InboxConfigurationException? configurationFault = null;
        var processed = 0;
        var leasesLost = 0;
        var index = 0;

        for (; index < messages.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                abortReason = InboxBatchAbortReason.Cancelled;
                InboxProcessorLogs.BatchCancelled(_logger, messages.Count - index);
                break;
            }

            var message = messages[index];
            var key = new InboxMessageKey(message.MessageId, message.ConsumerType);

            try
            {
                switch (await HandleAsync(message, key, claim.Token, cancellationToken)
                    .ConfigureAwait(false))
                {
                    case HandleResult.Settled:
                        processed++;
                        break;

                    case HandleResult.SucceededWithoutCommit:
                        // The handler succeeded but committed nothing, so the mark must be
                        // written by the deferred path. Still owned: the token is intact.
                        deferred.Add(InboxOutcome.Processed(key));
                        break;

                    case HandleResult.LeaseLost:
                        leasesLost++;
                        break;

                    default:
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Released, not retried: the handler may well have committed before the
                // cancellation landed. Charging a retry would penalise a message for a
                // shutdown it had no part in.
                abortReason = InboxBatchAbortReason.Cancelled;
                InboxProcessorLogs.BatchCancelled(_logger, messages.Count - index);
                break;
            }
            catch (InboxDependencyUnavailableException ex)
            {
                abortReason = InboxBatchAbortReason.DependencyUnavailable;
                InboxProcessorLogs.DependencyUnavailable(_logger, ex, messages.Count - index);
                break;
            }
            catch (InboxConfigurationException ex)
            {
                // A missing registration is a deployment defect, not a transient fault:
                // treating it as transient would dead-letter the whole queue over one missing
                // line in the composition root. The batch is settled below — every row
                // released untouched — and only then is the fault rethrown.
                abortReason = InboxBatchAbortReason.ConfigurationError;
                configurationFault = ex;
                InboxProcessorLogs.ServiceUnresolvable(_logger, ex, messages.Count - index);
                break;
            }
            catch (InboxPayloadException ex)
            {
                InboxProcessorLogs.PermanentFailure(
                    _logger, ex, message.MessageId.Value, message.ConsumerType);

                deferred.Add(InboxOutcome.DeadLetter(key, message.RetryCount, Truncate(ex.Message)));
            }
            catch (Exception ex)
            {
                deferred.Add(BuildTransientOutcome(message, key, ex));
            }
        }

        // Everything from `index` onward was claimed but never attempted — including the row
        // that triggered the break. Released, never Retry: an outage must not consume the
        // retry budget of rows it never reached.
        for (var i = index; i < messages.Count; i++)
        {
            var pending = messages[i];
            deferred.Add(InboxOutcome.Released(
                new InboxMessageKey(pending.MessageId, pending.ConsumerType)));
        }

        // Settle before anything else can throw: a stranded lease is worse than a late
        // exception, because it blocks its row for the whole LeaseDuration.
        await SettleAsync(claim.Token, deferred).ConfigureAwait(false);

        if (configurationFault is not null)
        {
            // Rethrown so the hosting worker stops. Backing off on a missing registration
            // would retry forever a condition that cannot fix itself without a redeployment.
            throw configurationFault;
        }

        return Summarize(deferred, messages.Count, processed, leasesLost, abortReason);
    }

    /// <summary>Resolves, stages and invokes one message inside its own execution scope.</summary>
    private async ValueTask<HandleResult> HandleAsync(
        InboxMessage message,
        InboxMessageKey key,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        // Registry lookup first: an unregistered consumer is permanent by construction, and
        // there is no point paying for an execution scope to discover it.
        if (!_registry.TryGetInvoker(message.ConsumerType, out var entry))
        {
            throw new InboxPayloadException(
                $"Unknown consumer type '{message.ConsumerType}'. It is absent from the handler " +
                "registry, so no number of retries will resolve it. Call " +
                "AddMessageHandler<THandler, TEvent>() at startup, then requeue.");
        }

        var ctx = new MessageCtx
        {
            TenantId = message.TenantId,
            CorrelationId = message.CorrelationId?.Value.ToString(),

            // DERIVED from this row, not copied off it — the outbox does the same and for the same
            // reason: everything the handler stages was caused by delivering THIS message, so the
            // cause is its identity. message.CausationId names what caused the row being handled,
            // which is one hop too far up.
            //
            // MessageId, never RowId. RowId is the local surrogate primary key ADR-MSG-017
            // introduced so the claim filters on a single column; it is meaningless outside this
            // table and to every other process. MessageId is the end-to-end identity the producer
            // assigned, and is the only one that answers "which message caused this" downstream.
            CausationId = message.MessageId.Value.ToString(),
        };

        await using var scope = await _executionScopeFactory
            .CreateScopeAsync(ctx, cancellationToken)
            .ConfigureAwait(false);

        var evt = Deserialize(message);

        var handler = Resolve(scope.ServiceProvider, entry.HandlerType);
        var settlement = ResolveSettlement(scope.ServiceProvider);

        // Staged BEFORE invoking, so the mark is part of whatever unit of work the handler
        // commits. Staging only — the handler's UoW owns the transaction boundary.
        var owned = await settlement
            .StageProcessedAsync(key, claimToken, cancellationToken)
            .ConfigureAwait(false);

        if (!owned)
        {
            // The lease expired and another processor owns this row. Invoking the handler now
            // would duplicate side effects the new owner is about to produce. Silent success is
            // forbidden, so this is surfaced and the row is left entirely alone.
            InboxProcessorLogs.LeaseLostBeforeHandler(
                _logger, message.MessageId.Value, message.ConsumerType);
            return HandleResult.LeaseLost;
        }

        try
        {
            await entry.Invoker(handler, evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException && settlement.IsLeaseLost(ex))
        {
            // The lease expired mid-handler. ClaimToken is mapped as a concurrency token, so the
            // handler's commit matched zero rows and its whole transaction rolled back — business
            // side effects and the staged mark together. Another processor owns the row now, so
            // nothing is written here: a retry outcome would be a no-op anyway, filtering on a
            // token that is no longer ours.
            //
            // Checked BEFORE the post-commit branch below, and it has to be: a lost lease leaves
            // the staged entry Modified, so IsMarkUncommitted is true and that branch would not
            // catch it. It would fall through to the transient path and consume a retry for a row
            // this processor no longer owns.
            InboxProcessorLogs.LeaseLostDuringHandler(
                _logger, ex, message.MessageId.Value, message.ConsumerType);

            return HandleResult.LeaseLost;
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException && !settlement.IsMarkUncommitted(key))
        {
            // The handler committed — mark included — and then threw. The work is durable and
            // the row is already Processed, so this is a post-commit fault, not a failure to
            // process. Retrying it would be wrong in principle and a no-op in practice: the
            // committed transaction cleared the claim token, so the retry write would match
            // zero rows and produce a misleading "lease expired" warning.
            InboxProcessorLogs.PostCommitFault(
                _logger, ex, message.MessageId.Value, message.ConsumerType);

            return HandleResult.Settled;
        }

        // The handler returned, but did it commit? A handler that performs no database work
        // never calls SaveChanges, so the staged mark was never persisted: the row would replay
        // on every pass and eventually dead-letter despite every invocation succeeding.
        if (settlement.IsMarkUncommitted(key))
        {
            InboxProcessorLogs.HandlerDidNotCommit(
                _logger, message.MessageId.Value, message.ConsumerType);

            return HandleResult.SucceededWithoutCommit;
        }

        return HandleResult.Settled;
    }

    /// <summary>Deserializes the payload, treating every failure as permanent.</summary>
    /// <remarks>
    /// Deserialization is CPU-bound over an in-memory string: it has no transient failure mode.
    /// A malformed payload therefore means the persisted row can never be read, which is the
    /// definition of a poison message — yet the previous implementation let those exceptions
    /// fall through to the transient branch and burn the whole retry budget on a verdict already
    /// fixed at the first attempt. The cleaner long-term shape is a typed exception on
    /// <see cref="IMessageSerializer"/>; until that contract changes, wrapping here is the
    /// honest fix.
    /// </remarks>
    private IIntegrationEvent Deserialize(InboxMessage message)
    {
        object? deserialized;

        try
        {
            deserialized = _serializer.Deserialize(message.Payload, message.EventType);
        }
        catch (Exception ex)
        {
            throw new InboxPayloadException(
                $"Payload for EventType '{message.EventType}' could not be deserialized. " +
                "The persisted row must change before this can succeed.",
                ex);
        }

        // The narrowing is part of the contract: the inbox only ever holds integration events.
        return deserialized as IIntegrationEvent
            ?? throw new InboxPayloadException(
                $"Payload for EventType '{message.EventType}' deserialized to null or to a type " +
                "that is not an IIntegrationEvent. The persisted row must change before this " +
                "can succeed.");
    }

    /// <summary>What one message attempt produced.</summary>
    private enum HandleResult
    {
        /// <summary>Handled and settled inside the handler's own transaction.</summary>
        Settled,

        /// <summary>Handled, but nothing committed the staged mark. Needs a deferred write.</summary>
        SucceededWithoutCommit,

        /// <summary>Skipped: the lease was already lost before the handler ran.</summary>
        LeaseLost,
    }

    /// <summary>Resolves a handler, converting a missing registration into a typed fault.</summary>
    /// <remarks>
    /// The previous implementation resolved the handler <b>outside</b> its try block, so an
    /// unregistered handler threw straight out of the batch loop: the whole batch died, every
    /// lease was stranded until expiry, and the worker logged it as transient and retried
    /// forever. The try here wraps the resolution call and nothing else, so the classification
    /// is structural rather than a guess at exception text.
    /// </remarks>
    private static object Resolve(IServiceProvider serviceProvider, Type handlerType)
    {
        try
        {
            return serviceProvider.GetRequiredService(handlerType);
        }
        catch (InvalidOperationException ex)
        {
            throw new InboxConfigurationException(
                $"Handler '{handlerType.FullName}' is present in the message registry but not in " +
                "the service provider. Retrying will not help.",
                ex);
        }
    }

    private static IInboxSettlementStore ResolveSettlement(IServiceProvider serviceProvider)
    {
        try
        {
            return serviceProvider.GetRequiredService<IInboxSettlementStore>();
        }
        catch (InvalidOperationException ex)
        {
            throw new InboxConfigurationException(
                $"{nameof(IInboxSettlementStore)} is not registered in the execution scope. " +
                "Without it the inbox cannot settle inside the handler transaction, and " +
                "transactionally atomic processing silently degrades to at-least-once.",
                ex);
        }
    }

    private InboxOutcome BuildTransientOutcome(
        InboxMessage message, InboxMessageKey key, Exception ex)
    {
        var nextRetryCount = message.RetryCount + 1;
        var error = Truncate(ex.Message);

        if (nextRetryCount >= _options.MaxRetries)
        {
            InboxProcessorLogs.RetriesExhausted(
                _logger, ex, message.MessageId.Value, message.ConsumerType, _options.MaxRetries);

            return InboxOutcome.DeadLetter(key, nextRetryCount, error);
        }

        var backoff = ApplyJitter(ComputeBackoffCeiling(nextRetryCount));
        var nextRetryAtUtc = _timeProvider.GetUtcNow().Add(backoff);

        InboxProcessorLogs.TransientFailure(
            _logger, ex, message.MessageId.Value, message.ConsumerType,
            nextRetryCount, _options.MaxRetries, nextRetryAtUtc);

        return InboxOutcome.Retry(key, nextRetryCount, nextRetryAtUtc, error);
    }

    /// <summary>
    /// The deterministic half of the back-off: <c>2^retryCount</c> seconds, capped at
    /// <see cref="InboxProcessorOptions.MaxRetryBackoff"/>.
    /// </summary>
    /// <remarks>
    /// Back-off is retry policy, not persistence — the previous implementation computed it
    /// inside the store, where it could not be tested without a database. The exponent is
    /// clamped before shifting so a corrupted retry count cannot overflow; the cap is reached
    /// long before the clamp in any realistic configuration. Kept separate from
    /// <see cref="ApplyJitter"/> so the exponential curve can be asserted against exact values.
    /// </remarks>
    internal TimeSpan ComputeBackoffCeiling(int retryCount)
    {
        var exponent = Math.Clamp(retryCount, 0, 20);
        var backoff = TimeSpan.FromSeconds(1L << exponent);

        return backoff > _options.MaxRetryBackoff ? _options.MaxRetryBackoff : backoff;
    }

    /// <summary>Applies full jitter: a uniform draw over <c>[0, ceiling]</c>.</summary>
    /// <remarks>
    /// Without it the back-off is deterministic, so with several processor instances the rows
    /// that fail together — which is exactly what a dependency outage produces — all retry at
    /// the same instant, and keep doing so on every subsequent attempt. Full jitter spreads them
    /// across the whole interval. The inbox previously had no jitter at all; this matches the
    /// outbox, which is what the older "symmetric with the outbox" comment claimed but no longer
    /// described.
    /// </remarks>
    internal TimeSpan ApplyJitter(TimeSpan ceiling) =>
        ceiling <= TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)(ceiling.Ticks * _random.NextDouble()));

    /// <summary>Persists deferred outcomes on an independent short timeout.</summary>
    /// <remarks>
    /// Deliberately not linked to the caller's token. Had a shutdown cancelled this write, every
    /// lease in the batch would stay held until expiry. A brief flush window on shutdown is
    /// strictly cheaper.
    /// </remarks>
    private async ValueTask SettleAsync(Guid claimToken, List<InboxOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        using var settleCts = new CancellationTokenSource(_options.OutcomeFlushTimeout);

        try
        {
            var written = await _store
                .ApplyOutcomesAsync(claimToken, outcomes, settleCts.Token)
                .ConfigureAwait(false);

            if (written < outcomes.Count)
            {
                // Silent success is forbidden: fewer rows than outcomes means those rows no
                // longer carry this batch's token.
                InboxProcessorLogs.PartialSettlement(_logger, written, outcomes.Count);
            }
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: leases expire on their own and the rows are re-claimed.
            // Letting this escape would take down the hosting BackgroundService over a
            // condition the inbox is designed to survive.
            InboxProcessorLogs.SettlementFailed(_logger, ex, outcomes.Count);
        }
    }

    private static InboxBatchResult Summarize(
        List<InboxOutcome> deferred,
        int claimedCount,
        int processed,
        int leasesLost,
        InboxBatchAbortReason abortReason)
    {
        var retried = 0;
        var deadLettered = 0;
        var released = 0;

        foreach (var outcome in deferred)
        {
            switch (outcome.Kind)
            {
                case InboxOutcomeKind.Processed: processed++; break;
                case InboxOutcomeKind.Retry: retried++; break;
                case InboxOutcomeKind.DeadLetter: deadLettered++; break;
                case InboxOutcomeKind.Released: released++; break;
                default: break;
            }
        }

        return new InboxBatchResult(
            claimedCount, processed, retried, deadLettered, released, leasesLost, abortReason);
    }

    private string? Truncate(string? value) =>
        value is null || value.Length <= _options.MaxErrorMessageLength
            ? value
            : value[.._options.MaxErrorMessageLength];
}
