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
/// longer head the claim (#188). On a row with a tenant, that verdict is first the tenant's: only
/// that tenant's rows of the endpoint are deferred, to the end of its own back-off, and the endpoint
/// goes down for every tenant only when the probe fails — a second tenant down before any success
/// (#124, <see cref="TenantBackoff"/>). Cancellation and a configuration fault release every row not
/// yet attempted; the rows handled before keep their outcomes.
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
/// batch and rethrows <see cref="OutboxConfigurationException"/>; the loop backs off and reports it
/// (1402) on every cycle: it will not fix itself without a redeployment. A registration the container can supply but cannot activate —
/// <c>GetService</c> throws <see cref="InvalidOperationException"/>, most often because
/// <see cref="IMessageTransport"/> is missing behind a transport dispatcher — defers that endpoint's
/// rows (that tenant's rows of it, for a row with a tenant), costs no retry budget and is not
/// rethrown: the batch reports <see cref="OutboxBatchAbortReason.DispatcherActivationFailed"/>, and
/// the endpoint, or the tenant, is tried again once its back-off ends.
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
/// <b>Ordering.</b> The loop is sequential, so messages are dispatched in claim order — except while a
/// tenant is in back-off on an endpoint: the rows of tenants with no back-off entry are then attempted
/// first, then the others, those that failed least first (#124). Nothing in the model expresses an
/// ordering requirement, so this is a property of the implementation, not a guarantee of the
/// contract. Parallelising it requires a partition key on <see cref="OutboxMessage"/> first.
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
    private readonly OutboxMetrics? _metrics;
    private readonly string? _storeTag;

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
    /// <param name="metrics">The outbox's instruments (#126). Null only for a processor built by hand, which records nothing.</param>
    public OutboxProcessor(
        IOutboxProcessorStore store,
        IExecutionScopeFactory executionScopeFactory,
        OutboxProcessorOptions options,
        TimeProvider timeProvider,
        Random random,
        ILogger<OutboxProcessor> logger,
        MessageStoreKey? storeKey = null,
        OutboxEndpointBackoff? endpointBackoff = null,
        OutboxMetrics? metrics = null)
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
        _metrics = metrics;
        _storeTag = storeKey?.Value;
    }

    /// <inheritdoc />
    public async ValueTask<OutboxBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // An endpoint in back-off is left out of the claim (#188): its rows are neither claimed nor
        // rewritten while it is down, and the other endpoint's rows behind them are reached. So is a
        // tenant in back-off on an endpoint (#124 A7), on that endpoint only: a store whose rows all carry
        // one tenant would otherwise claim and defer its whole backlog each time the back-off ends. With
        // no tenant in back-off both lists are the shared empty array.
        var start = _timeProvider.GetUtcNow();
        _endpointBackoff.Prune(start);
        var excluded = _endpointBackoff.BlockedAt(start);
        var transportTenants = (excluded & OutboxEndpoints.Transport) != 0
            ? []
            : _endpointBackoff.TenantsOf(OutboxDispatchers.Transport).BlockedAt(start);
        var localTenants = (excluded & OutboxEndpoints.Local) != 0
            ? []
            : _endpointBackoff.TenantsOf(OutboxDispatchers.Local).BlockedAt(start);
        var claim = await _store
            .ClaimBatchAsync(batchSize, _options.LockDuration, excluded, transportTenants, localTenants, cancellationToken)
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

        // While a tenant is in back-off the rows are attempted in another order (P1): the tenants with
        // no entry first, in claim order — the probe — then the tenants with one, those that failed
        // least first. Null — the happy path — is claim order, and allocates nothing.
        var order = AttemptOrder(messages);
        var position = 0;

        // An endpoint that is down — its transport unreachable, its dispatcher not activatable, a
        // local consumer's dependency out — has its rows deferred, unattempted and uncounted, until
        // its back-off ends; the other endpoint's rows are still dispatched (#188). The back-off is
        // the store's, kept across cycles, so a down endpoint is tried once per back-off period and
        // its stuck rows never head the next claim. Event 1010 reports, per endpoint, what the batch
        // deferred, once the batch is done; 1011 the same per tenant (#124).
        var deferred = default(DeferralTally);
        List<TenantTally>? tenantsDeferred = null;
        var handedOver = false;

        // What the batch settles, per endpoint and outcome, for microkit.outbox.messages.settled (#126):
        // counted here, where each row's endpoint is known, and recorded once the settlement wrote it all.
        var settledTally = default(OutboxMetrics.SettledTally);

        for (; position < messages.Count; position++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                abortReason = OutboxBatchAbortReason.Cancelled;
                OutboxProcessorLogs.BatchCancelled(_logger, messages.Count - position);
                break;
            }

            var message = order is null ? messages[position] : messages[order[position]];
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
                settledTally.Add(endpointKey, OutboxOutcomeKind.Deferred);
                continue;
            }

            // A row of a tenant in back-off on its endpoint (#124 D1): deferred unattempted to the end
            // of the tenant's back-off — the rest of the batch in which the tenant was blocked. Only a
            // row of a tenant the claim was asked to leave out tells the loop to wait, as for an
            // endpoint: a store that does not honour the tenant filter (A7).
            if (endpointKey is not null
                && message.TenantId is { } blockedTenant
                && _endpointBackoff.TenantsOf(endpointKey) is { IsEmpty: false } tenants
                && tenants.IsBlocked(blockedTenant, _timeProvider.GetUtcNow(), out var tenantUntil))
            {
                handedOver |= Array.IndexOf(
                    endpointKey is OutboxDispatchers.Local ? localTenants : transportTenants,
                    blockedTenant) >= 0;
                outcomes.Add(OutboxOutcome.Deferred(message.Id, tenantUntil));
                TenantTally.Add(ref tenantsDeferred, endpointKey, blockedTenant, tenantUntil);
                settledTally.Add(endpointKey, OutboxOutcomeKind.Deferred);
                continue;
            }

            try
            {
                await DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                if (_metrics is { DispatchDelayEnabled: true } metrics)
                {
                    metrics.RecordDispatchDelay(_timeProvider.GetUtcNow() - message.CreatedAtUtc, _storeTag, endpointKey);
                }

                outcomes.Add(OutboxOutcome.Published(message.Id));
                settledTally.Add(endpointKey, OutboxOutcomeKind.Published);
                if (endpointKey is not null)
                {
                    _endpointBackoff.Succeeded(endpointKey, message.TenantId);
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
                OutboxProcessorLogs.BatchCancelled(_logger, messages.Count - position);
                break;
            }
            catch (OutboxTransportUnavailableException ex)
            {
                abortReason = FirstOf(abortReason, OutboxBatchAbortReason.TransportUnavailable);
                OutboxProcessorLogs.TransportUnavailable(_logger, ex, message.Endpoint);
                Down(message, endpointKey, outcomes, ref deferred, ref tenantsDeferred);
                settledTally.Add(endpointKey, OutboxOutcomeKind.Deferred);
            }
            catch (OutboxConfigurationException ex)
            {
                // A missing registration is a deployment defect, not a transient fault:
                // treating it as transient would dead-letter the whole queue over one missing
                // line in the composition root. The batch is settled below — the rest of it
                // released untouched — and only then is the fault rethrown: the loop reports it
                // (1402) and backs off on the idle curve, without stranding a single lease.
                abortReason = OutboxBatchAbortReason.ConfigurationError;
                configurationFault = ex;
                OutboxProcessorLogs.DispatchMisconfigured(_logger, ex, messages.Count - position);
                break;
            }
            catch (OutboxDispatcherActivationException ex)
            {
                // A dispatcher that is registered but cannot be activated — the container's
                // InvalidOperationException, the only type ResolveDispatcher tags: neither a
                // per-message retry nor a configuration fault. Costs no retry budget and is NOT
                // rethrown. A transient condition, or a redeployment that supplies a missing
                // dependency, drains the queue once the back-off ends.
                //
                // Activation runs in THIS message's scope, built from its TenantId, so it can fail for
                // one tenant only (#124): the row's tenant is put in back-off on the endpoint, and only
                // the probe failing — a second tenant down before any success — takes the endpoint
                // down for every tenant (Down). A tenant that never recovers is tried once per its own
                // back-off and holds no other tenant's rows; the operator's exit is
                // IOutboxAdminStore.DeadLetterPendingAsync. The log names the row.
                abortReason = FirstOf(abortReason, OutboxBatchAbortReason.DispatcherActivationFailed);
                OutboxProcessorLogs.DispatcherActivationFailed(
                    _logger,
                    ex.InnerException ?? ex,
                    message.Id.Value,
                    message.TenantId,
                    message.Endpoint);
                Down(message, endpointKey, outcomes, ref deferred, ref tenantsDeferred);
                settledTally.Add(endpointKey, OutboxOutcomeKind.Deferred);
            }
            catch (OutboxPayloadException ex)
            {
                OutboxProcessorLogs.PermanentFailure(_logger, ex, message.Id.Value, message.EventType);

                outcomes.Add(OutboxOutcome.DeadLetter(
                    message.Id, message.RetryCount, Truncate(ex.Message)));
                settledTally.Add(endpointKey, OutboxOutcomeKind.DeadLetter);
            }
            catch (Exception ex)
            {
                var transient = BuildTransientOutcome(message, ex);
                outcomes.Add(transient);
                settledTally.Add(endpointKey, transient.Kind);
            }
        }

        // Every row from `position` onward in the attempt order was claimed but never attempted —
        // including the message that triggered the break (cancellation, a configuration fault),
        // whichever pass was running (C1). Released, never Retry: an outage must not consume the retry
        // budget of messages it never reached.
        for (var i = position; i < messages.Count; i++)
        {
            outcomes.Add(OutboxOutcome.Released((order is null ? messages[i] : messages[order[i]]).Id));
        }

        Debug.Assert(
            outcomes.Count == messages.Count,
            "Every claimed message must carry exactly one outcome, or leases will be stranded.");

        // Settle before anything else can throw: a stranded lease is worse than a late
        // exception, because it blocks its message for the whole LockDuration.
        var settled = await SettleAsync(claim.Token, outcomes).ConfigureAwait(false);

        // Counted once the settlement wrote every outcome, as 1010. A failed settlement's rows come back and
        // are counted then, once; a partial one (1007) is not counted at all — the store says how many rows
        // it wrote, not which. Released rows were never counted.
        if (settled)
        {
            _metrics?.RecordSettled(_storeTag, in settledTally);
        }

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

        if (settled && tenantsDeferred is not null)
        {
            foreach (var tally in tenantsDeferred)
            {
                OutboxProcessorLogs.TenantDeferred(
                    _logger,
                    tally.EndpointKey is OutboxDispatchers.Local ? OutboxMessage.LocalEndpoint : null,
                    tally.Tenant,
                    tally.Count,
                    tally.From,
                    tally.Until);
            }
        }

        if (configurationFault is not null)
        {
            // Rethrown: the loop reports it (1402) and backs off on the idle curve. Only a host stop
            // ends a loop, and the condition cannot fix itself without a redeployment.
            throw configurationFault;
        }

        return Summarize(outcomes, messages.Count, abortReason) with { HandedOverInBackOff = handedOver };
    }

    /// <summary>
    /// Settles a row that met an endpoint-down verdict, from the time the failure happened — not the
    /// batch's start, which a long batch leaves in the past.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row with no tenant puts its endpoint in back-off, as #188 did. A row with one puts its tenant in
    /// back-off on the endpoint (#124 D1) and is deferred to the end of the tenant's; the endpoint stays
    /// open. Only the probe failing (<see cref="TenantBackoff.OutageScope.Shared"/>) puts the endpoint in
    /// back-off too (D2), and the row waits for the later of the two.
    /// </para>
    /// </remarks>
    private void Down(
        OutboxMessage message,
        string? endpointKey,
        List<OutboxOutcome> outcomes,
        ref DeferralTally deferred,
        ref List<TenantTally>? tenantsDeferred)
    {
        var key = endpointKey ?? OutboxDispatchers.Transport;
        var now = _timeProvider.GetUtcNow();

        if (message.TenantId is not { } tenant)
        {
            var endpointOnly = _endpointBackoff.Block(key, now);
            outcomes.Add(OutboxOutcome.Deferred(message.Id, endpointOnly));
            deferred.Add(endpointKey, endpointOnly);
            return;
        }

        var scope = _endpointBackoff.TenantsOf(key).OnDown(tenant, now, out var tenantUntil);
        if (scope is not TenantBackoff.OutageScope.Shared)
        {
            outcomes.Add(OutboxOutcome.Deferred(message.Id, tenantUntil));
            TenantTally.Add(ref tenantsDeferred, key, tenant, tenantUntil);
            return;
        }

        var endpointUntil = _endpointBackoff.Block(key, now);
        var until = endpointUntil > tenantUntil ? endpointUntil : tenantUntil;
        outcomes.Add(OutboxOutcome.Deferred(message.Id, until));
        deferred.Add(endpointKey, until);
    }

    /// <summary>
    /// The order the batch's rows are attempted in, as indexes into the claim; <see langword="null"/> for
    /// claim order (P1).
    /// </summary>
    /// <remarks>
    /// With no tenant in back-off on either endpoint, claim order, and nothing is allocated. Otherwise
    /// the rows whose tenant has no entry on their endpoint come first, in claim order — so the probe is
    /// a tenant not known to fail — then the rows of tenants with one, the smallest current delay first
    /// (the tenants that failed least), claim order on ties: two tenants that never recover cannot head
    /// every batch and hold a third (#124). One array and one list per batch, never per row.
    /// </remarks>
    private int[]? AttemptOrder(IReadOnlyList<OutboxMessage> messages)
    {
        if (_endpointBackoff.TenantsOf(OutboxDispatchers.Transport).IsEmpty
            && _endpointBackoff.TenantsOf(OutboxDispatchers.Local).IsEmpty)
        {
            return null;
        }

        var order = new int[messages.Count];
        var known = new List<(long DelayTicks, int Index)>();
        var next = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (OutboxDispatchers.KeyOf(message) is { } key
                && message.TenantId is { } tenant
                && _endpointBackoff.TenantsOf(key).TryGetDelay(tenant, out var delay))
            {
                known.Add((delay.Ticks, i));
            }
            else
            {
                order[next++] = i;
            }
        }

        // ValueTuple's own ordering: the delay, then the claim index.
        known.Sort();
        foreach (var (_, index) in known)
        {
            order[next++] = index;
        }

        return order;
    }

    /// <summary>What one batch deferred for one tenant on one endpoint, for event 1011.</summary>
    private sealed class TenantTally(string endpointKey, string tenant)
    {
        public string EndpointKey { get; } = endpointKey;

        public string Tenant { get; } = tenant;

        public int Count { get; private set; }

        public DateTimeOffset From { get; private set; }

        public DateTimeOffset Until { get; private set; }

        public static void Add(ref List<TenantTally>? tallies, string endpointKey, string tenant, DateTimeOffset until)
        {
            tallies ??= [];
            TenantTally? tally = null;
            foreach (var candidate in tallies)
            {
                if (candidate.EndpointKey == endpointKey && string.Equals(candidate.Tenant, tenant, StringComparison.Ordinal))
                {
                    tally = candidate;
                    break;
                }
            }

            if (tally is null)
            {
                tally = new TenantTally(endpointKey, tenant);
                tallies.Add(tally);
            }

            if (tally.Count++ == 0 || until < tally.From)
            {
                tally.From = until;
            }

            if (until > tally.Until)
            {
                tally.Until = until;
            }
        }
    }

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
    /// <b>A tenant-scoped activation failure holds that tenant only.</b> This method runs in the scope of
    /// the message being dispatched, built from its <c>TenantId</c>, so activation can see the message
    /// and throw <see cref="InvalidOperationException"/> for one tenant only. The verdict below then
    /// defers that tenant's rows of the endpoint (#124); a de-provisioned tenant is tried once per its own
    /// back-off and never dead-letters on its own, and no other tenant waits for it. Only the probe
    /// failing — a second tenant down before any success — holds the endpoint.
    /// </para>
    /// <para>
    /// <c>GetService</c> separates the two verdicts structurally rather than by inspecting an exception:
    /// it returns <see langword="null"/> only when nothing is registered, and throws when something is
    /// registered and cannot be activated.
    /// <list type="bullet">
    ///   <item><b>Null</b> is <see cref="OutboxConfigurationException"/>: the batch is released,
    ///         settled and rethrown, and the loop backs off reporting it (1402). A missing
    ///         registration will not fix itself without a redeployment.</item>
    ///   <item><b>An <see cref="InvalidOperationException"/></b> is wrapped in
    ///         <see cref="OutboxDispatcherActivationException"/>, which the batch loop catches by type:
    ///         the row's endpoint — or its tenant on the endpoint — is deferred, no retry budget moves,
    ///         nothing is rethrown, and it is tried again once its back-off ends. The <c>try</c> wraps <c>GetService</c> and nothing else, and
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
    /// <b>Per tenant, then endpoint-wide, rather than per message.</b> For a cause every message shares —
    /// a missing <see cref="IMessageTransport"/> — a per-message verdict would charge N retry budgets
    /// for one cause and, at <c>MaxRetries</c>, dead-letter the queue over one line in a composition
    /// root. Not every cause is shared: a tenant-scoped one fails for some messages only, so the verdict
    /// holds the row's tenant first and the endpoint only once the probe fails (#124). The inbox does
    /// not copy it (#188): a guard that cannot be activated fails that one delivery, redelivered, or that
    /// one retry, counted — never the batch.
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

    private string? Truncate(string? value) => ErrorText.Cut(value, _options.MaxErrorMessageLength);
}
