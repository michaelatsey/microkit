using System.Diagnostics;

using MicroKit.Messaging.Stores;

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
/// known to take an endpoint down — its transport unreachable, its dispatcher not activatable —
/// defers that endpoint's rows to the end of its back-off (<see cref="OutboxEndpointBackoff"/>),
/// unattempted and uncounted, instead of failing every message in turn and burning the queue's retry
/// budget during an outage; the other endpoint's rows are still dispatched, and the deferred rows no
/// longer head the claim (#188). Cancellation and a configuration fault release the rest of the
/// batch; the rows handled before keep their outcomes.
/// Everything unrecognised stays transient: a library that guesses permanence wrongly loses
/// messages, so only proven permanence dead-letters.
/// </para>
/// <para>
/// Resolving the dispatcher yields three verdicts, and the container decides between them rather
/// than an inspection of its exception: <c>GetService</c> returning <see langword="null"/> is a
/// configuration fault; <c>GetService</c> throwing <see cref="InvalidOperationException"/> takes the
/// row's endpoint down at no retry cost; any other type it throws, and anything thrown once the dispatcher
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
/// <see cref="IMessageTransport"/> is missing behind a transport dispatcher — defers that endpoint's
/// rows, costs no retry budget and is not rethrown: the batch reports
/// <see cref="OutboxBatchAbortReason.DispatcherActivationFailed"/>, and the endpoint is tried again
/// once its back-off ends.
/// </para>
/// <para>
/// <b>Delivery semantics.</b> At-least-once. A crash between dispatch and the settlement
/// write redelivers the whole batch, so consumers must be idempotent. Settlement is
/// deferred to the end of the batch, so the replay window is one batch rather than one
/// message.
/// </para>
/// <para>
/// <b>Deferred settlement costs a re-send, never a re-run.</b> The settlement write and the
/// dispatch are two separate transactions: the outcome is buffered and <c>ApplyOutcomesAsync</c>
/// runs afterwards on the batch-scoped store. A crash in between dispatches every message of the
/// batch again. Every consumer sits behind an inbox row, so that costs duplication only: a transport
/// row is re-sent and the receiver's inbox absorbs it; a local row (#188 D7) is delivered again and
/// each consumer's row, already <c>Processed</c> or <c>Failed</c>, skips it. No handler runs twice
/// for a replayed batch.
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
    // from the per-message IExecutionScope. It is the store this processor drains, resolved by
    // the store's key from its loop's scope (#188); a dynamic store set is #191's.
    private readonly IOutboxProcessorStore _store;
    private readonly IExecutionScopeFactory _executionScopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly MessageStoreKey? _storeKey;
    private readonly OutboxEndpointBackoff _endpointBackoff;

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
    /// <param name="storeKey">
    /// The store this processor drains, written on every dispatch scope so a publication made there
    /// is enlisted in the same store. Null only for a processor built by hand.
    /// </param>
    /// <param name="endpointBackoff">
    /// The store's endpoint back-off, shared by every cycle's processor. Null only for a processor
    /// built by hand, which then gets one of its own.
    /// </param>
    public OutboxProcessor(
        IOutboxProcessorStore store,
        IExecutionScopeFactory executionScopeFactory,
        OutboxProcessorOptions options,
        TimeProvider timeProvider,
        Random random,
        ILogger<OutboxProcessor> logger,
        MessageStoreKey? storeKey = null,
        OutboxEndpointBackoff? endpointBackoff = null)
    {
        _store = store;
        _executionScopeFactory = executionScopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _random = random;
        _logger = logger;
        _storeKey = storeKey;
        _endpointBackoff = endpointBackoff
            ?? new OutboxEndpointBackoff(options.PollingInterval, options.TransportUnavailableBackoff);
    }

    /// <inheritdoc />
    public async ValueTask<OutboxBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // An endpoint in back-off is left out of the claim (#188): its rows are neither claimed nor
        // rewritten while it is down, and the other endpoint's rows behind them are reached.
        var excluded = _endpointBackoff.BlockedAt(_timeProvider.GetUtcNow());
        var claim = await _store
            .ClaimBatchAsync(batchSize, _options.LockDuration, excluded, cancellationToken)
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

        // An endpoint that is down — its transport unreachable, its dispatcher not activatable, a
        // local consumer's dependency out — has its rows deferred, unattempted and uncounted, until
        // its back-off ends; the other endpoint's rows are still dispatched (#188). The back-off is
        // the store's, kept across cycles, so a down endpoint is tried once per back-off period and
        // its stuck rows never head the next claim. Event 1010 reports, per endpoint, what the batch
        // deferred, once the batch is done.
        var deferred = default(DeferralTally);
        var handedOver = false;

        for (; index < messages.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                abortReason = OutboxBatchAbortReason.Cancelled;
                OutboxProcessorLogs.BatchCancelled(_logger, messages.Count - index);
                break;
            }

            var message = messages[index];
            var endpointKey = OutboxDispatchers.KeyOf(message);
            if (endpointKey is not null
                && _endpointBackoff.IsBlocked(endpointKey, _timeProvider.GetUtcNow(), out var blockedUntil))
            {
                // Handed over although its endpoint is in back-off: by a back-off that began after the
                // claim, or by a store that does not honour the claim's endpoint filter.
                abortReason = FirstOf(abortReason, OutboxBatchAbortReason.EndpointInBackOff);

                // Only a row whose endpoint the claim was asked to leave out tells the loop to wait:
                // a store that does not honour the filter, against the claim's contract. A back-off
                // that began in this batch does not: its deferred rows wait out their NextRetryAtUtc,
                // a conforming store leaves the endpoint out of the next claim, and a store that does
                // not honour the filter hands over the rest of its backlog, which
                // sets the flag then if that batch comes within the back-off — at most one immediate
                // batch more per back-off start.
                handedOver |= (excluded & EndpointOf(endpointKey)) != 0;
                outcomes.Add(OutboxOutcome.Deferred(message.Id, blockedUntil));
                deferred.Add(endpointKey, blockedUntil);
                continue;
            }

            try
            {
                await DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                outcomes.Add(OutboxOutcome.Published(message.Id));
                if (endpointKey is not null)
                {
                    _endpointBackoff.Clear(endpointKey);
                }
            }
            catch (Exception ex) when (HostStop.Is(ex, cancellationToken))
            {
                // Released, not retried: the dispatch may well have completed before the
                // cancellation landed. Charging a retry would penalise a message for a
                // shutdown it had no part in. A provider disposed under the dispatch while the host
                // stops — the scope's creation included — is the same shutdown, never a failure
                // (#128); the same exception while the host runs is a failure (HostStop).
                abortReason = OutboxBatchAbortReason.Cancelled;
                OutboxProcessorLogs.BatchCancelled(_logger, messages.Count - index);
                break;
            }
            catch (OutboxTransportUnavailableException ex)
            {
                var until = BlockEndpoint(endpointKey);
                abortReason = FirstOf(abortReason, OutboxBatchAbortReason.TransportUnavailable);
                OutboxProcessorLogs.TransportUnavailable(_logger, ex, message.Endpoint);
                outcomes.Add(OutboxOutcome.Deferred(message.Id, until));
                deferred.Add(endpointKey, until);
            }
            catch (OutboxConfigurationException ex)
            {
                // A missing registration is a deployment defect, not a transient fault:
                // treating it as transient would dead-letter the whole queue over one missing
                // line in the composition root. The batch is settled below — the rest of it
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
                // per-message retry nor a configuration fault. The endpoint is down: this row and its
                // endpoint's later rows are deferred to the end of its back-off, costing no retry
                // budget, and NOT rethrown; the other endpoint's rows are still dispatched. A
                // transient condition, or a redeployment that supplies a missing dependency, drains
                // the queue once the back-off ends.
                //
                // KNOWN DEFECT (#124). Activation runs in THIS message's scope, built from its
                // TenantId, so it can fail for one tenant only. A cause that never clears — a
                // de-provisioned tenant — fails again each time the endpoint is tried: its back-off
                // grows toward its ceiling, or restarts from the base delay whenever another
                // tenant's row of the endpoint gets through first. Either way the row is never
                // dead-lettered, and the endpoint's rows wait while it fails. The log names the row
                // so an operator can find it; the fix is owed.
                var until = BlockEndpoint(endpointKey);
                abortReason = FirstOf(abortReason, OutboxBatchAbortReason.DispatcherActivationFailed);
                OutboxProcessorLogs.DispatcherActivationFailed(
                    _logger,
                    ex.InnerException ?? ex,
                    message.Id.Value,
                    message.TenantId,
                    message.Endpoint);
                outcomes.Add(OutboxOutcome.Deferred(message.Id, until));
                deferred.Add(endpointKey, until);
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
        // message that triggered the break (cancellation, a configuration fault). Released, never
        // Retry: an outage must not consume the retry budget of messages it never reached.
        for (var i = index; i < messages.Count; i++)
        {
            outcomes.Add(OutboxOutcome.Released(messages[i].Id));
        }

        Debug.Assert(
            outcomes.Count == messages.Count,
            "Every claimed message must carry exactly one outcome, or leases will be stranded.");

        // Settle before anything else can throw: a stranded lease is worse than a late
        // exception, because it blocks its message for the whole LockDuration.
        var settled = await SettleAsync(claim.Token, outcomes).ConfigureAwait(false);

        // 1010 reports a deferral that was written: a failed or partial settlement is reported by
        // its own event, and its rows wait out their lease instead.
        if (settled && deferred.Transport > 0)
        {
            OutboxProcessorLogs.EndpointDeferred(
                _logger, null, deferred.Transport, deferred.TransportFrom, deferred.TransportUntil);
        }

        if (settled && deferred.Local > 0)
        {
            OutboxProcessorLogs.EndpointDeferred(
                _logger, OutboxMessage.LocalEndpoint, deferred.Local, deferred.LocalFrom, deferred.LocalUntil);
        }

        if (configurationFault is not null)
        {
            // Rethrown so the hosting worker stops. Backing off on a missing registration
            // would retry forever a condition that cannot fix itself without a redeployment.
            throw configurationFault;
        }

        return Summarize(outcomes, messages.Count, abortReason) with { HandedOverInBackOff = handedOver };
    }

    /// <summary>
    /// Puts the row's endpoint in back-off, from the time the failure happened — not the batch's
    /// start, which a long batch leaves in the past — and returns when it ends.
    /// </summary>
    private DateTimeOffset BlockEndpoint(string? endpointKey)
        => _endpointBackoff.Block(endpointKey ?? OutboxDispatchers.Transport, _timeProvider.GetUtcNow());

    /// <summary>The <see cref="OutboxEndpoints"/> flag of a dispatcher key.</summary>
    private static OutboxEndpoints EndpointOf(string endpointKey)
        => endpointKey is OutboxDispatchers.Local ? OutboxEndpoints.Local : OutboxEndpoints.Transport;

    /// <summary>
    /// The batch's reason is the first endpoint verdict it met; each endpoint's own is logged with
    /// its endpoint (1004, 1009).
    /// </summary>
    private static OutboxBatchAbortReason FirstOf(OutboxBatchAbortReason current, OutboxBatchAbortReason next)
        => current is OutboxBatchAbortReason.None ? next : current;

    /// <summary>What one batch deferred, per endpoint, for event 1010.</summary>
    /// <remarks>
    /// Rows of one endpoint can wait until different times: a back-off can end mid-batch and the
    /// next failure start another. The tally keeps the earliest and the latest.
    /// </remarks>
    private struct DeferralTally
    {
        public int Transport;
        public int Local;
        public DateTimeOffset TransportFrom;
        public DateTimeOffset TransportUntil;
        public DateTimeOffset LocalFrom;
        public DateTimeOffset LocalUntil;

        public void Add(string? endpointKey, DateTimeOffset until)
        {
            if (endpointKey is OutboxDispatchers.Local)
            {
                Widen(ref Local, ref LocalFrom, ref LocalUntil, until);
            }
            else
            {
                Widen(ref Transport, ref TransportFrom, ref TransportUntil, until);
            }
        }

        private static void Widen(ref int count, ref DateTimeOffset from, ref DateTimeOffset to, DateTimeOffset until)
        {
            if (count++ == 0 || until < from)
            {
                from = until;
            }

            if (until > to)
            {
                to = until;
            }
        }
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
            // the CausationId value object and the envelope causation all documented a link
            // that was never built. The reentrant hop is where that stopped being harmless — a
            // contract published by a notification handler demonstrably has a cause, and it is the
            // row this scope is dispatching.
            CausationId = message.Id.Value.ToString(),
        };

        // Scope creation sits inside the caller's try on purpose: a tenant-aware factory
        // may perform I/O to resolve a per-tenant connection, and that failure is a
        // dispatch failure like any other. Outside, it would abort the batch without
        // settling a single outcome — stranding every lease.
        await using var scope = await _executionScopeFactory
            .CreateScopeAsync(ctx, cancellationToken)
            .ConfigureAwait(false);

        // Written on the scope this processor received, deliberately not passed to the factory:
        // a host may supply its own IExecutionScopeFactory. A publication made in this scope — a
        // notification handler's — is enlisted in the store this row came from.
        if (_storeKey is not null && scope.ServiceProvider.GetService<DeliveryStoreHolder>() is { } holder)
        {
            holder.Store = _storeKey;
        }

        var dispatcher = ResolveDispatcher(scope.ServiceProvider, message);

        await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
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
    /// the endpoint-wide verdict below, which holds them all.
    /// </para>
    /// <para>
    /// <b>That stall is narrowed, not closed.</b> This method runs in the scope of the message being
    /// dispatched, built from its <c>TenantId</c>, so
    /// activation can see the message and throw <see cref="InvalidOperationException"/> for one tenant
    /// only. A de-provisioned tenant does not recover on its own: each time its endpoint's back-off
    /// ends the row fails again, and the verdict below holds that endpoint's rows of every tenant: a
    /// known defect, the fix owed by #124. The other endpoint's rows are not held.
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
    ///         the row's endpoint is deferred, no retry budget moves, nothing is rethrown, and the
    ///         endpoint is tried again once its back-off ends. The <c>try</c> wraps <c>GetService</c> and nothing else, and
    ///         <c>GetService</c> never throws for an absent registration, so the wrapper tags an
    ///         activation failure — it classifies nothing as configuration.</item>
    ///   <item><b>Cancellation and disposal are not tagged.</b> An
    ///         <see cref="OperationCanceledException"/> does not derive from
    ///         <see cref="InvalidOperationException"/>, so it never reaches the <c>catch</c>: it takes the
    ///         loop's cancellation arm when the token is cancelled, and the transient arm otherwise. An
    ///         <see cref="ObjectDisposedException"/> does derive from it, and the filter excludes it: the
    ///         provider going away is the host shutting down, not the composition failing: while the host
    ///         stops, the batch loop's <c>HostStop</c> arm releases the row with its attempt count
    ///         unchanged (#128); while it runs, it is a transient failure of that message.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Endpoint-wide rather than per message.</b> For a cause every message shares — a missing
    /// <see cref="IMessageTransport"/> — a per-message verdict would charge N retry budgets for one
    /// cause and, at <c>MaxRetries</c>, dead-letter the queue over one line in a composition root. Not
    /// every cause is shared: a tenant-scoped one fails for some messages only, and deferring the
    /// endpoint for it is the known defect above. The inbox does not copy it (#188): a guard that
    /// cannot be activated fails that one delivery, redelivered, or that one retry, counted — never the
    /// batch.
    /// </para>
    /// </remarks>
    private static IOutboxDispatcher ResolveDispatcher(IServiceProvider serviceProvider, OutboxMessage message)
    {
        // The row's endpoint names its dispatcher (#188 D7). A value this build cannot route is
        // permanent for this deployment: re-reading the row will never change it.
        var key = OutboxDispatchers.KeyOf(message)
            ?? throw new OutboxPayloadException(
                $"Outbox message {message.Id} carries endpoint '{message.Endpoint}', which this build " +
                "cannot route: a row is dispatched to the transport (no endpoint) or to the local " +
                $"endpoint ('{OutboxMessage.LocalEndpoint}'). The row was written by another version " +
                "or edited by hand.");

        IOutboxDispatcher? dispatcher;

        try
        {
            dispatcher = serviceProvider.GetKeyedService<IOutboxDispatcher>(key);
        }
        catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
        {
            throw new OutboxDispatcherActivationException(ex);
        }

        return dispatcher
            ?? throw new OutboxConfigurationException(
                $"Outbox message {message.Id} is bound for the {(key is OutboxDispatchers.Transport ? "transport" : "local endpoint")}, " +
                "but its dispatcher is not registered. The store engine registers both dispatchers with every " +
                "store, so this container was composed outside AddMessageStore. Retrying will not help.");
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
    /// <returns><see langword="true"/> when every outcome was written.</returns>
    private async ValueTask<bool> SettleAsync(Guid claimToken, List<OutboxOutcome> outcomes)
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
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: leases expire on their own and the batch is redelivered.
            // Letting this escape would take down the hosting BackgroundService over a
            // condition the outbox is designed to survive.
            OutboxProcessorLogs.SettlementFailed(_logger, ex, outcomes.Count);
            return false;
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
                // A deferred row was not attempted: reported with the released ones.
                case OutboxOutcomeKind.Released or OutboxOutcomeKind.Deferred: released++; break;
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
