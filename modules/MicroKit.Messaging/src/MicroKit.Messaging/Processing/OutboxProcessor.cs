using System.Diagnostics;

using MicroKit.Messaging.Outbox;

using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Topology-agnostic outbox batch engine. Atomically claims a batch of dispatchable
/// <see cref="OutboxMessage"/> rows, dispatches each through
/// <see cref="IOutboxDispatcher"/> inside its own execution scope, and settles every
/// disposition with one <c>ApplyOutcomesAsync</c> call at the end of the batch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Isolation.</b> One <see cref="IExecutionScope"/> per message — never shared across
/// a batch. A <c>DbContext</c> fault on message N cannot corrupt message N+1, and
/// <c>TenantId</c> context is fresh every time. This guarantee is why the loop is not
/// collapsed into a single scope despite the cost.
/// </para>
/// <para>
/// <b>Failure classification.</b> Failures <i>known</i> to be permanent are dead-lettered
/// on first sight rather than after <c>MaxRetries</c> pointless dispatches, and a failure
/// known to be fatal to the batch abandons it and releases the remainder untouched instead
/// of failing every message in turn and burning the whole queue's retry budget during an
/// outage. Everything unrecognised stays transient: a library that guesses permanence
/// wrongly loses messages, so only proven permanence dead-letters.
/// </para>
/// <para>
/// Resolving the dispatcher yields three verdicts, and the container decides between them rather
/// than an inspection of its exception: <c>GetService</c> returning <see langword="null"/> is a
/// configuration fault; <c>GetService</c> throwing <see cref="InvalidOperationException"/> is
/// batch-fatal at no retry cost; any other type it throws, and anything thrown once the dispatcher
/// exists, is classified as above.
/// </para>
/// <para>
/// <b>Misconfiguration.</b> A missing <see cref="IOutboxDispatcher"/> registration is not
/// a transient fault. A null test detects it, not a <c>catch</c>, and it abandons the batch, so a
/// missing line in the composition root cannot silently consume the retry budget of every queued
/// message.
/// </para>
/// <para>
/// <b>Configuration failure and activation failure are not synonyms.</b> A registration the
/// container cannot supply at all — <c>GetService</c> returns <see langword="null"/> — abandons the
/// batch, rethrows <see cref="OutboxConfigurationException"/> and stops the worker: it will not fix
/// itself without a redeployment. A registration the container can supply but cannot activate —
/// <c>GetService</c> throws <see cref="InvalidOperationException"/>, most often because
/// <see cref="IMessageTransport"/> is missing behind a transport dispatcher — abandons the batch
/// too, but costs no retry budget and is not rethrown: the batch reports
/// <see cref="OutboxBatchAbortReason.DispatcherActivationFailed"/>, the worker backs off, and the
/// next cycle tries again.
/// </para>
/// <para>
/// <b>Delivery semantics.</b> At-least-once. A crash between dispatch and the settlement
/// write redelivers the whole batch, so consumers must be idempotent. Settlement is
/// deferred to the end of the batch, so the replay window is one batch rather than one
/// message.
/// </para>
/// <para>
/// <b>Known defect — deferred settlement is not transactional, and the batch window is not
/// free.</b> The settlement write and any work the dispatcher's target performed are two
/// separate transactions: the target commits inside the per-message scope, the outcome is
/// buffered, and <c>ApplyOutcomesAsync</c> runs afterwards on the batch-scoped store. A crash
/// in between replays every message in the batch.
/// <list type="bullet">
///   <item>Where each consumer sits behind an <b>inbox</b>, that costs duplication only — the
///         receiver writes one row per consumer, the unique index absorbs the redelivery, and no
///         handler runs twice.</item>
///   <item>Where the dispatcher invokes a handler <b>in-process with no inbox row</b>, it does
///         not. <c>MediatROutboxDispatcher</c> publishes a notification through
///         <c>IPublisher.Publish</c>, and notification handlers have no per-consumer inbox
///         (ADR-MSG-009): a replay re-runs every one of them and re-writes whatever they
///         wrote. A handler that projects into a table produces its rows a second time.</item>
/// </list>
/// The consumer side already solved this — <see cref="IInboxSettlementStore"/> stages the
/// processed mark into the handler's own unit of work, so the mark and the side effects commit
/// together. The outbox needs the counterpart: a settlement store that stages the
/// <c>Published</c> mark into the transaction the dispatch target commits, instead of writing
/// it in a separate one afterwards. That changes <see cref="IOutboxProcessorStore"/> and is
/// deferred to its own lot; until then the mitigation is the documented one — <b>notification
/// handlers on this path must be idempotent</b>, and the guarantee is at-least-once, not
/// transactionally atomic as it is on the inbox.
/// </para>
/// <para>
/// <b>Ordering.</b> The loop is sequential, so messages are dispatched in claim order.
/// Nothing in the model expresses an ordering requirement, so this is a property of the
/// implementation, not a guarantee of the contract. Parallelising it requires a
/// partition key on <see cref="OutboxMessage"/> first.
/// </para>
/// </remarks>
internal sealed class OutboxProcessor : IOutboxProcessor
{
    // Batch-scoped by design (ADR-MSG-002 shared-DB cross-tenant reservation): NOT resolved
    // from the per-message IExecutionScope. Per-tenant DB resolution is the deferred
    // PerTenantOutboxCoordinator's responsibility, not the processor's.
    private readonly IOutboxProcessorStore _store;
    private readonly IExecutionScopeFactory _executionScopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly ILogger<OutboxProcessor> _logger;

    /// <summary>Initializes a new <see cref="OutboxProcessor"/>.</summary>
    /// <param name="store">Claim and settlement operations. Batch-scoped, not per-message.</param>
    /// <param name="executionScopeFactory">Creates the per-message execution scope.</param>
    /// <param name="options">Batch size, lease duration, retry ceiling and back-off bounds.</param>
    /// <param name="timeProvider">Clock. Injected so back-off is testable without a wall clock.</param>
    /// <param name="random">
    /// Jitter source. Injected for the same reason as <paramref name="timeProvider"/>: a
    /// deterministic substitute pins the back-off curve to exact values instead of a range.
    /// </param>
    /// <param name="logger">Logger.</param>
    public OutboxProcessor(
        IOutboxProcessorStore store,
        IExecutionScopeFactory executionScopeFactory,
        OutboxProcessorOptions options,
        TimeProvider timeProvider,
        Random random,
        ILogger<OutboxProcessor> logger)
    {
        _store = store;
        _executionScopeFactory = executionScopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _random = random;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<OutboxBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var claim = await _store
            .ClaimBatchAsync(batchSize, _options.LockDuration, cancellationToken)
            .ConfigureAwait(false);

        if (claim.Count == 0)
        {
            return OutboxBatchResult.Empty;
        }

        OutboxProcessorLogs.BatchClaimed(_logger, claim.Count);

        var messages = claim.Messages;
        var outcomes = new List<OutboxOutcome>(messages.Count);
        var abortReason = OutboxBatchAbortReason.None;
        OutboxConfigurationException? configurationFault = null;
        var index = 0;

        for (; index < messages.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                abortReason = OutboxBatchAbortReason.Cancelled;
                OutboxProcessorLogs.BatchCancelled(_logger, messages.Count - index);
                break;
            }

            var message = messages[index];

            try
            {
                await DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                outcomes.Add(OutboxOutcome.Published(message.Id));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Released, not retried: the dispatch may well have completed before the
                // cancellation landed. Charging a retry would penalise a message for a
                // shutdown it had no part in.
                abortReason = OutboxBatchAbortReason.Cancelled;
                OutboxProcessorLogs.BatchCancelled(_logger, messages.Count - index);
                break;
            }
            catch (OutboxTransportUnavailableException ex)
            {
                abortReason = OutboxBatchAbortReason.TransportUnavailable;
                OutboxProcessorLogs.TransportUnavailable(_logger, ex, messages.Count - index);
                break;
            }
            catch (OutboxConfigurationException ex)
            {
                // A missing registration is a deployment defect, not a transient fault:
                // treating it as transient would dead-letter the whole queue over one missing
                // line in the composition root. The batch is settled below — every message
                // released untouched — and only then is the fault rethrown, so the worker can
                // stop without stranding a single lease.
                abortReason = OutboxBatchAbortReason.ConfigurationError;
                configurationFault = ex;
                OutboxProcessorLogs.DispatchMisconfigured(_logger, ex, messages.Count - index);
                break;
            }
            catch (OutboxDispatcherActivationException ex)
            {
                // A dispatcher that is registered but cannot be activated — the container's
                // InvalidOperationException, the only type ResolveDispatcher tags: neither a
                // per-message retry nor a configuration fault. The batch is abandoned and released
                // untouched, costing no retry budget, and NOT rethrown: the worker backs off and the
                // next cycle tries again, so a transient condition, or a redeployment that supplies a
                // missing dependency, drains the queue.
                //
                // KNOWN DEFECT (ADR-MSG-019). Activation runs in THIS message's scope, built from its
                // TenantId, so it can fail for one tenant only. A cause that never clears — a
                // de-provisioned tenant — leaves this row oldest, heading every claim, and stalls the
                // outbox for every tenant with no dead-letter exit. The log names the row so an
                // operator can find it; the fix is owed.
                abortReason = OutboxBatchAbortReason.DispatcherActivationFailed;
                OutboxProcessorLogs.DispatcherActivationFailed(
                    _logger,
                    ex.InnerException ?? ex,
                    message.Id.Value,
                    message.TenantId,
                    message.MessageKind,
                    messages.Count - index);
                break;
            }
            catch (OutboxPayloadException ex)
            {
                OutboxProcessorLogs.PermanentFailure(_logger, ex, message.Id.Value, message.EventType);

                outcomes.Add(OutboxOutcome.DeadLetter(
                    message.Id, message.RetryCount, Truncate(ex.Message)));
            }
            catch (Exception ex)
            {
                outcomes.Add(BuildTransientOutcome(message, ex));
            }
        }

        // Everything from `index` onward was claimed but never attempted — including the
        // message that triggered the break. Released, never Retry: an outage must not
        // consume the retry budget of messages it never reached.
        for (var i = index; i < messages.Count; i++)
        {
            outcomes.Add(OutboxOutcome.Released(messages[i].Id));
        }

        Debug.Assert(
            outcomes.Count == messages.Count,
            "Every claimed message must carry exactly one outcome, or leases will be stranded.");

        // Settle before anything else can throw: a stranded lease is worse than a late
        // exception, because it blocks its message for the whole LockDuration.
        await SettleAsync(claim.Token, outcomes).ConfigureAwait(false);

        if (configurationFault is not null)
        {
            // Rethrown so the hosting worker stops. Backing off on a missing registration
            // would retry forever a condition that cannot fix itself without a redeployment.
            throw configurationFault;
        }

        return Summarize(outcomes, messages.Count, abortReason);
    }

    private async ValueTask DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var ctx = new MessageCtx
        {
            TenantId = message.TenantId,
            // Null-conditional deliberately kept. OutboxMessage enforces no invariant on
            // CorrelationId, so a row written by anything other than OutboxMessageFactory can
            // carry null. The factory already substitutes CorrelationId.New() when the execution
            // context has none, but the entity itself does not guarantee it.
            CorrelationId = message.CorrelationId?.Value.ToString(),

            // DERIVED from this row, not copied off it. Everything staged inside this scope was
            // caused by dispatching THIS message, so the cause is message.Id.
            // message.CausationId names what caused the row being dispatched — one hop too far up.
            //
            // Copying it through made every descendant inherit one ancestor's causation, and since
            // nothing in the module ever assigns a causation at the root, that ancestor's value is
            // null: the chain was null on every row, on every path, while OutboxMessage.CausationId,
            // the CausationId value object and MessageEnvelope.CausationId all documented a link
            // that was never built. The reentrant hop is where that stopped being harmless — a
            // contract published by a notification handler demonstrably has a cause, and it is the
            // row this scope is dispatching.
            //
            // Same identity OriginMessageHolder carries two statements below, for a different
            // purpose: the origin is a REPLAY KEY and must be exact, causation is a TRACE LINK and
            // degrades to null when unparseable (OutboxMessageFactory.ResolveCausation). They are
            // kept separate because one may never be allowed to fall back and the other must.
            CausationId = message.Id.Value.ToString(),
        };

        // Scope creation sits inside the caller's try on purpose: a tenant-aware factory
        // may perform I/O to resolve a per-tenant connection, and that failure is a
        // dispatch failure like any other. Outside, it would abort the batch without
        // settling a single outcome — stranding every lease.
        await using var scope = await _executionScopeFactory
            .CreateScopeAsync(ctx, cancellationToken)
            .ConfigureAwait(false);

        // Stamped on the scope this processor received, deliberately not passed to the factory.
        // A host may supply its own IExecutionScopeFactory, and an implementation that forgot to
        // carry the value would not throw — it would leave the origin null, which silently
        // disables deduplication for every contract this dispatch publishes, because nulls are
        // distinct in UX_OutboxMessages_Origin_ContractName. Doing it here removes the chance.
        StampOrigin(scope.ServiceProvider, message.Id);

        var dispatcher = ResolveDispatcher(scope.ServiceProvider);

        await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Names the row being dispatched, so a notification handler that publishes an integration
    /// event records which dispatch produced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OriginMessageHolder"/> is registered by <c>AddMicroKitMessaging()</c>. A scope whose
    /// provider does not have it was built from a container this composition does not control — most
    /// likely by a custom <see cref="IExecutionScopeFactory"/> — which is a deployment defect, not a
    /// transient fault. It is therefore <see cref="OutboxConfigurationException"/>: the batch is
    /// released and the worker stops. Classifying it as transient would spend the retry budget of
    /// every queued message on a condition no retry can change, and carrying on without it would
    /// leave the origin null, which silently disables the replay key for every contract this dispatch
    /// publishes.
    /// </para>
    /// <para>
    /// <c>GetService</c> and a null test rather than <c>GetRequiredService</c> inside a <c>catch</c>,
    /// because only <see langword="null"/> means nothing is registered. Here no reachable input tells
    /// the two forms apart: the holder is an internal marker with no constructor dependencies, so its
    /// activation cannot fail — which is also why this method, unlike
    /// <see cref="ResolveDispatcher"/>, has no activation verdict.
    /// </para>
    /// <para>
    /// ⚠ <b>If the holder ever gains a dependency, revisit this method.</b> An activation failure
    /// would then propagate unwrapped into the per-message transient arm — the misclassification
    /// <see cref="ResolveDispatcher"/> exists to avoid.
    /// </para>
    /// </remarks>
    private static void StampOrigin(IServiceProvider serviceProvider, MessageId originId)
    {
        var holder = serviceProvider.GetService<OriginMessageHolder>()
            ?? throw new OutboxConfigurationException(
                $"{nameof(OriginMessageHolder)} could not be resolved from the execution scope. " +
                "It is registered by AddMicroKitMessaging(), so the likeliest cause is an " +
                "IExecutionScopeFactory returning a scope built from a different container. " +
                "Without it a published integration event cannot record the dispatch that " +
                "produced it, and the replay key that stops a redelivery duplicating it is " +
                "silently inactive. Retrying will not help.");

        holder.OriginMessageId = originId;
    }

    /// <summary>
    /// Resolves the dispatcher, converting a MISSING registration into a typed fault and tagging the
    /// container's activation failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetService</c> and a null test decide "not registered", deliberately, and <b>not</b>
    /// <c>GetRequiredService</c> inside a <c>catch (InvalidOperationException)</c>. The two read alike
    /// and classify very differently: resolving <see cref="IOutboxDispatcher"/> activates a graph — a
    /// <c>TransportOutboxDispatcher</c> takes <see cref="IMessageTransport"/> through its
    /// constructor, and a decorator activates its keyed inner — so a <c>catch</c> around the
    /// resolution spans that entire graph rather than the registration lookup it appears to guard.
    /// </para>
    /// <para>
    /// <b>The case that matters is the common one.</b> A transport dispatcher registered with no
    /// <see cref="IMessageTransport"/> behind it fails right here: the container reports the dependency
    /// it cannot supply as an <see cref="InvalidOperationException"/>, and so does anything else in the
    /// graph that throws that type. Caught as a configuration fault, each was reported as a missing
    /// registration that is in fact present, told "retrying will not help", and stopped the worker.
    /// </para>
    /// <para>
    /// <b>Only <see cref="InvalidOperationException"/> is tagged, because it is the only type that was
    /// ever conflated.</b> It is what the container throws for an unsatisfiable dependency, and what the
    /// catch-based form read as a missing registration; separating those two is this method's whole job.
    /// Every other type thrown while the graph is built keeps the classification it had under that form:
    /// a typed <see cref="OutboxTransportUnavailableException"/>, <see cref="OutboxConfigurationException"/>
    /// or <see cref="OutboxPayloadException"/> reaches its own arm in the batch loop, and anything else
    /// reaches the transient arm — one message, one retry, and the rest of the batch runs. A wider
    /// catch would move a tenant lookup failing with its own type, a <see cref="KeyNotFoundException"/>
    /// say, from a per-message retry that dead-letters while every other tenant keeps publishing into
    /// the batch-wide verdict below, which stalls them all.
    /// </para>
    /// <para>
    /// <b>That stall is narrowed, not closed.</b> This method runs in the scope of the message being
    /// dispatched, built from its <c>TenantId</c> and stamped with its origin before the call, so
    /// activation can see the message and throw <see cref="InvalidOperationException"/> for one tenant
    /// only. A de-provisioned tenant does not recover on its own: its row stays oldest, heads every
    /// claim, and the verdict below stalls the outbox for every tenant. ADR-MSG-019 records that as a
    /// known defect with the fix owed.
    /// </para>
    /// <para>
    /// <c>GetService</c> separates the two verdicts structurally rather than by inspecting an exception:
    /// it returns <see langword="null"/> only when nothing is registered, and throws when something is
    /// registered and cannot be activated.
    /// <list type="bullet">
    ///   <item><b>Null</b> is <see cref="OutboxConfigurationException"/>: the batch is released,
    ///         settled and rethrown, and the worker stops. A missing registration will not fix itself
    ///         without a redeployment.</item>
    ///   <item><b>An <see cref="InvalidOperationException"/></b> is wrapped in
    ///         <see cref="OutboxDispatcherActivationException"/>, which the batch loop catches by type:
    ///         the batch is released and settled, no retry budget moves, nothing is rethrown, and the
    ///         next cycle tries again. The <c>try</c> wraps <c>GetService</c> and nothing else, and
    ///         <c>GetService</c> never throws for an absent registration, so the wrapper tags an
    ///         activation failure — it classifies nothing as configuration.</item>
    ///   <item><b>Cancellation and disposal are not tagged.</b> An
    ///         <see cref="OperationCanceledException"/> does not derive from
    ///         <see cref="InvalidOperationException"/>, so it never reaches the <c>catch</c>: it takes the
    ///         loop's cancellation arm when the token is cancelled, and the transient arm otherwise. An
    ///         <see cref="ObjectDisposedException"/> does derive from it, and the filter excludes it: the
    ///         provider going away is the host shutting down, not the composition failing. Not being an
    ///         <see cref="OperationCanceledException"/>, it reaches the transient arm and costs that
    ///         message one retry — exactly as one thrown by <c>CreateScopeAsync</c> does. Whether
    ///         shutdown should release rather than retry is owed (ADR-MSG-019).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Batch-wide rather than per message.</b> For a cause every message shares — a missing
    /// <see cref="IMessageTransport"/> — a per-message verdict would charge N retry budgets for one
    /// cause and, at <c>MaxRetries</c>, dead-letter the queue over one line in a composition root. Not
    /// every cause is shared: a tenant-scoped one fails for some messages only, and releasing the batch
    /// for it is the known defect above. <c>EnvelopeReceiver.ResolveWriter</c> lets the same container
    /// verdict propagate, because the receiver holds one envelope and no batch to abandon.
    /// <c>InboxProcessor.ResolveSettlement</c> retries the one row although the drain processor owns a
    /// batch and a retry budget too, so ownership does not explain this method's difference from it.
    /// ADR-MSG-019 records all three.
    /// </para>
    /// </remarks>
    private static IOutboxDispatcher ResolveDispatcher(IServiceProvider serviceProvider)
    {
        IOutboxDispatcher? dispatcher;

        try
        {
            dispatcher = serviceProvider.GetService<IOutboxDispatcher>();
        }
        catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
        {
            throw new OutboxDispatcherActivationException(ex);
        }

        return dispatcher
            ?? throw new OutboxConfigurationException(
                $"{nameof(IOutboxDispatcher)} is not registered, so no outbox row can be dispatched. " +
                "Call AddTransportDispatcher() on the MessagingBuilder — a broker provider's " +
                "Add{Provider}Transport() calls it — or AddMediatRDomainEvents() for domain-event " +
                "notifications. Retrying will not help.");
    }

    private OutboxOutcome BuildTransientOutcome(OutboxMessage message, Exception ex)
    {
        var nextRetryCount = message.RetryCount + 1;
        var error = Truncate(ex.Message);

        if (nextRetryCount >= _options.MaxRetries)
        {
            OutboxProcessorLogs.RetriesExhausted(
                _logger, ex, message.Id.Value, message.EventType, _options.MaxRetries);

            return OutboxOutcome.DeadLetter(message.Id, nextRetryCount, error);
        }

        var backoff = ApplyJitter(ComputeBackoffCeiling(nextRetryCount));
        var nextRetryAtUtc = _timeProvider.GetUtcNow().Add(backoff);

        OutboxProcessorLogs.TransientFailure(
            _logger, ex, message.Id.Value, message.EventType,
            nextRetryCount, _options.MaxRetries, nextRetryAtUtc);

        return OutboxOutcome.Retry(message.Id, nextRetryCount, nextRetryAtUtc, error);
    }

    /// <summary>
    /// The deterministic half of the back-off: <c>2^retryCount</c> seconds, capped at
    /// <see cref="OutboxProcessorOptions.MaxRetryBackoff"/>.
    /// </summary>
    /// <remarks>
    /// Moved here from the store: back-off is retry policy, not persistence, and it is
    /// worth being unit-testable without a database. The exponent is clamped before
    /// shifting so a corrupted retry count cannot overflow; the cap is reached long
    /// before the clamp in any realistic configuration. Kept separate from
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
    /// Without it the back-off is deterministic, so with several processor instances the
    /// messages that fail together — which is exactly what a broker outage produces — all
    /// retry at the same instant, and keep doing so on every subsequent attempt. Full jitter
    /// spreads them across the whole interval.
    /// </remarks>
    internal TimeSpan ApplyJitter(TimeSpan ceiling) =>
        ceiling <= TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)(ceiling.Ticks * _random.NextDouble()));

    /// <summary>Settles the batch on an independent short timeout.</summary>
    /// <remarks>
    /// Deliberately not linked to the caller's token. Had a shutdown cancelled this write,
    /// every lease in the batch would stay held until expiry and every already-dispatched
    /// message would be dispatched again. A brief flush window on shutdown is strictly
    /// cheaper than that.
    /// </remarks>
    private async ValueTask SettleAsync(Guid claimToken, List<OutboxOutcome> outcomes)
    {
        using var settleCts = new CancellationTokenSource(_options.OutcomeFlushTimeout);

        try
        {
            var written = await _store
                .ApplyOutcomesAsync(claimToken, outcomes, settleCts.Token)
                .ConfigureAwait(false);

            if (written < outcomes.Count)
            {
                // Silent success is forbidden: fewer rows than outcomes means leases were
                // lost mid-batch and another processor now owns those messages.
                OutboxProcessorLogs.PartialSettlement(_logger, written, outcomes.Count);
            }
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: leases expire on their own and the batch is redelivered.
            // Letting this escape would take down the hosting BackgroundService over a
            // condition the outbox is designed to survive.
            OutboxProcessorLogs.SettlementFailed(_logger, ex, outcomes.Count);
        }
    }

    private static OutboxBatchResult Summarize(
        List<OutboxOutcome> outcomes,
        int claimedCount,
        OutboxBatchAbortReason abortReason)
    {
        var published = 0;
        var retried = 0;
        var deadLettered = 0;
        var released = 0;

        foreach (var outcome in outcomes)
        {
            switch (outcome.Kind)
            {
                case OutboxOutcomeKind.Published: published++; break;
                case OutboxOutcomeKind.Retry: retried++; break;
                case OutboxOutcomeKind.DeadLetter: deadLettered++; break;
                case OutboxOutcomeKind.Released: released++; break;
                default: break;
            }
        }

        return new OutboxBatchResult(
            claimedCount, published, retried, deadLettered, released, abortReason);
    }

    private string? Truncate(string? value) =>
        value is null || value.Length <= _options.MaxErrorMessageLength
            ? value
            : value[.._options.MaxErrorMessageLength];
}
