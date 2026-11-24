using System.Text;
using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The retry engine of one store's inbox: atomically claims a batch of rows whose last attempt
/// failed, runs each through the receive pipeline's guarded retry, and settles the failures.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never invokes a handler itself (#188 D6).</b> A retry goes through the same pipeline as a
/// delivery: a fresh scope and transaction per attempt, the row marked <c>Processed</c> in the
/// handler's own transaction, conditioned on this claim. A success therefore settles itself and
/// produces no outcome here.
/// </para>
/// <para>
/// <b>Claim before scope.</b> The batch is reserved before any execution scope is created. A
/// tenant-aware <see cref="IExecutionScopeFactory"/> may perform I/O to resolve a per-tenant
/// connection, and spending that on a row another loop already owns is waste.
/// </para>
/// <para>
/// <b>Failure classification.</b> A failure known to be permanent dead-letters on sight; a
/// dependency outage on a row with a tenant puts its (consumer, tenant) pair in back-off — and its
/// consumer, for every tenant, when its consumer probe failed — the rows it holds wait to the end of it
/// with no attempt counted, and the batch goes on (#124, #210); only the store probe failing (a second
/// tenant down before any success), a row with no tenant, or the store itself unreachable abandon the
/// batch and release the remainder with no attempt counted; a lost claim writes nothing; a
/// host stop releases the remainder with no attempt counted; everything else is transient, retried with
/// full-jitter back-off until <see cref="InboxProcessorOptions.MaxRetries"/>. Failures are settled in
/// one call at the end of the batch, each conditioned on the claim.
/// </para>
/// <para>
/// <b>Ordering.</b> Rows are retried in claim order — except while a pair or a consumer is in back-off:
/// the rows whose (consumer, tenant) pair has no back-off entry are then retried first, in claim order,
/// then the others, those whose pair failed least first (<see cref="InboxBackoff"/>). No order is
/// promised.
/// </para>
/// </remarks>
/// <param name="store">
/// Claim and settlement of failures. Batch-scoped by design (ADR-MSG-002 shared-DB cross-tenant
/// reservation): NOT resolved from a per-message scope. It is the store this processor drains,
/// resolved by the store's key from its loop's scope (#188) — prepared for its instance when the
/// store is partitioned (#191).
/// </param>
/// <param name="registry">Maps a consumer type and a message type to its handler and invoker.</param>
/// <param name="localConsumers">The local consumers (#188 D7), looked up when the bus has no handler of the pair.</param>
/// <param name="contracts">Carries each message type's contract name and version (#187).</param>
/// <param name="serializer">Deserializes the persisted payload.</param>
/// <param name="pipeline">Runs each retry, guarded.</param>
/// <param name="options">Batch size, lease, flush timeout.</param>
/// <param name="policy">Attempts and back-off.</param>
/// <param name="logger">Logger.</param>
/// <param name="storeKey">The store this processor drains.</param>
/// <param name="backoff">
/// The store's inbox back-off, shared with the deliveries guarded by the store (#124, #210). Null only
/// for a processor built by hand, which then gets one of its own.
/// </param>
/// <param name="instance">
/// The instance of a partitioned store this processor drains (#191): each retry's scopes are prepared
/// for it. Null for a single store.
/// </param>
internal sealed class InboxProcessor(
    IInboxProcessorStore store,
    MessageHandlerRegistry registry,
    LocalConsumerRegistry localConsumers,
    IntegrationEventRegistry contracts,
    IMessageSerializer serializer,
    MessageReceivePipeline pipeline,
    InboxProcessorOptions options,
    InboxRetryPolicy policy,
    ILogger<InboxProcessor> logger,
    MessageStoreKey storeKey,
    InboxBackoff? backoff = null,
    StoreInstanceState? instance = null)
    : IInboxProcessor
{
    private readonly InboxBackoff _backoff = backoff
        ?? new InboxBackoff(options.PollingInterval, options.DependencyUnavailableBackoff);

    /// <inheritdoc />
    public async ValueTask<InboxBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // The pairs and the consumers in back-off are left out of the claim (#124 A7, #210 D4): their rows
        // are neither claimed nor rewritten while their dependency is down, and the rows behind them are
        // reached. The shared InboxClaimExclusions.None while nothing is in back-off.
        var start = policy.Clock.GetUtcNow();
        _backoff.Prune(start);

        var claim = await store
            .ClaimBatchAsync(batchSize, options.LeaseDuration, _backoff.ExclusionsAt(start), cancellationToken)
            .ConfigureAwait(false);

        if (claim.Count == 0)
        {
            return InboxBatchResult.Empty;
        }

        InboxProcessorLogs.BatchClaimed(logger, claim.Count);

        var messages = claim.Messages;

        // Only failures and releases are buffered: a retry that succeeds wrote its own row in the
        // handler's transaction.
        var deferred = new List<InboxOutcome>(capacity: 8);
        var abortReason = InboxBatchAbortReason.None;
        InboxConfigurationException? configurationFault = null;
        var processed = 0;
        var leasesLost = 0;

        // While a pair or a consumer is in back-off, the rows whose pair has no entry are retried first
        // (#124 P1); null — the happy path — is claim order, and allocates nothing.
        var order = AttemptOrder(messages);
        var position = 0;

        // The rows a back-off deferred in this batch, unattempted or attempted with no attempt counted:
        // reported with the released ones, and by 2013 per (consumer, tenant) once settled; the consumers
        // this batch put in consumer-wide back-off, by 2015.
        var tenantDeferred = 0;
        List<(string Consumer, string Tenant, int Count, DateTimeOffset Until)>? tenantsDeferred = null;
        List<(string Consumer, DateTimeOffset Until)>? consumersHeld = null;

        for (; position < messages.Count; position++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                abortReason = InboxBatchAbortReason.Cancelled;
                InboxProcessorLogs.BatchCancelled(logger, messages.Count - position);
                break;
            }

            var message = order is null ? messages[position] : messages[order[position]];
            var key = new InboxMessageKey(message.MessageId, message.ConsumerType);

            // A row whose pair or consumer is in back-off (#124 D5, #210 D4): due again at the end of the
            // back-off, its attempt count and its error left as they are — the rest of the batch in which
            // it was blocked, or a row a delivery blocked since the claim (A7).
            if (message.TenantId is { } blocked
                && !_backoff.IsEmpty
                && _backoff.IsBlocked(message.ConsumerType, blocked, policy.Clock.GetUtcNow(), out var blockedUntil, out var pairHeld))
            {
                deferred.Add(InboxOutcome.Retry(key, message.RetryCount, blockedUntil, message.ErrorMessage));
                tenantDeferred++;
                if (pairHeld)
                {
                    // 2013 names the tenant: only for a pair that failed. A row its consumer's back-off holds
                    // alone is a tenant that never failed for it, which 2015 reports with the consumer.
                    Tally(ref tenantsDeferred, message.ConsumerType, blocked, blockedUntil);
                }

                continue;
            }

            RetryOutcome outcome;
            try
            {
                outcome = await RetryAsync(message, claim.Token, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (HostStop.Is(ex, cancellationToken))
            {
                outcome = new RetryOutcome(RetryResult.Unsettled, Error: ex);
            }
            catch (InboxConfigurationException ex)
            {
                // A store without its guard is a deployment defect: every row is released untouched
                // below, then the fault is rethrown: this store's loop reports it and backs off.
                abortReason = InboxBatchAbortReason.ConfigurationError;
                configurationFault = ex;
                InboxProcessorLogs.ServiceUnresolvable(logger, ex, messages.Count - position);
                break;
            }
            catch (Exception ex) when (ex is InboxPayloadException)
            {
                // The engine's own verdict before any attempt: an unknown consumer, an unreadable row.
                outcome = new RetryOutcome(RetryResult.Failed, DeliveryVerdict.Permanent, ex);
            }

            if (outcome.Result is RetryResult.Unsettled && !HostStop.Is(outcome.Error!, cancellationToken))
            {
                // The pipeline's shutdown test also accepts a provider disposed while the host runs
                // (the transport's rule). For a retry only a host stop is a stop (D10): anything
                // else is the attempt failing.
                outcome = new RetryOutcome(RetryResult.Failed, DeliveryVerdict.Transient, outcome.Error);
            }

            if (outcome.Result is RetryResult.Unsettled)
            {
                // Released, not retried: the handler may well have committed before the stop
                // landed. Charging an attempt would penalise a message for a shutdown it had no
                // part in.
                abortReason = InboxBatchAbortReason.Cancelled;
                InboxProcessorLogs.BatchCancelled(logger, messages.Count - position);
                break;
            }

            switch (outcome.Result)
            {
                case RetryResult.Processed:
                    processed++;
                    _backoff.Succeeded(message.ConsumerType, message.TenantId);
                    continue;

                case RetryResult.LeaseLost:
                    leasesLost++;
                    continue;

                default:
                    break;
            }

            if (outcome.Verdict is DeliveryVerdict.DependencyUnavailable)
            {
                if (!outcome.StoreUnreachable && message.TenantId is { } tenant)
                {
                    var scope = _backoff.OnDown(message.ConsumerType, tenant, policy.Clock.GetUtcNow(), out var tenantUntil);
                    if (scope is not InboxBackoff.OutageScope.Shared)
                    {
                        // The pair's dependency, or its consumer's (#124 D5, #210 D4): the row waits to the
                        // end of the back-off with no attempt counted, and the batch goes on.
                        deferred.Add(InboxOutcome.Retry(
                            key, message.RetryCount, tenantUntil, policy.Truncate(outcome.Error?.Message)));
                        tenantDeferred++;
                        Tally(ref tenantsDeferred, message.ConsumerType, tenant, tenantUntil);
                        if (scope is InboxBackoff.OutageScope.Consumer)
                        {
                            (consumersHeld ??= []).Add((message.ConsumerType, tenantUntil));
                        }

                        continue;
                    }
                }

                // The store unreachable, a row with no tenant, or the store probe failing (its pair is in
                // back-off now too): the cause is shared, and the batch is abandoned as before #124.
                _backoff.MarkSuspected();
                abortReason = InboxBatchAbortReason.DependencyUnavailable;
                InboxProcessorLogs.DependencyUnavailable(logger, outcome.Error!, messages.Count - position);
                break;
            }

            switch (outcome.Verdict)
            {
                case DeliveryVerdict.Permanent:
                    InboxProcessorLogs.PermanentFailure(
                        logger, outcome.Error!, message.MessageId.Value, message.ConsumerType);
                    // A permanent verdict counts no attempt: no number of attempts would change it.
                    deferred.Add(InboxOutcome.DeadLetter(
                        key, message.RetryCount, policy.Truncate(outcome.Error?.Message)));
                    continue;

                default:
                    deferred.Add(BuildTransientOutcome(message, key, outcome.Error!));
                    continue;
            }
        }

        // Every row from `position` onward in the attempt order was claimed but never attempted —
        // including the row that triggered the break — whichever pass was running (C1). Released,
        // never Retry: an outage must not consume the retry budget of rows it never reached.
        for (var i = position; i < messages.Count; i++)
        {
            var pending = order is null ? messages[i] : messages[order[i]];
            deferred.Add(InboxOutcome.Released(
                new InboxMessageKey(pending.MessageId, pending.ConsumerType)));
        }

        // Settle before anything else can throw: a stranded lease is worse than a late
        // exception, because it blocks its row for the whole LeaseDuration.
        var settled = await SettleAsync(claim.Token, deferred).ConfigureAwait(false);

        if (settled && consumersHeld is not null)
        {
            foreach (var (consumer, until) in consumersHeld)
            {
                InboxProcessorLogs.ConsumerInBackoff(logger, storeKey.Value, consumer, until);
            }
        }

        if (settled && tenantsDeferred is not null)
        {
            foreach (var (consumer, tenant, count, until) in tenantsDeferred)
            {
                InboxProcessorLogs.TenantDeferred(logger, storeKey.Value, consumer, tenant, count, until);
            }
        }

        if (configurationFault is not null)
        {
            throw configurationFault;
        }

        return Summarize(deferred, messages.Count, processed, leasesLost, tenantDeferred, abortReason);
    }

    /// <summary>
    /// The order the batch's rows are retried in, as indexes into the claim; <see langword="null"/> for
    /// claim order (#124 P1).
    /// </summary>
    /// <remarks>
    /// With nothing in back-off, claim order, and nothing is allocated. Otherwise the rows whose (consumer,
    /// tenant) pair has no entry come first, in claim order — a probe is a pair not known to fail — then the
    /// rows of pairs with one, the smallest current delay first, claim order on ties. One array and one list
    /// per batch, never per row.
    /// </remarks>
    private int[]? AttemptOrder(IReadOnlyList<InboxMessage> messages)
    {
        if (_backoff.IsEmpty)
        {
            return null;
        }

        var order = new int[messages.Count];
        var known = new List<(long DelayTicks, int Index)>();
        var next = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].TenantId is { } tenant && _backoff.TryGetDelay(messages[i].ConsumerType, tenant, out var delay))
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

    /// <summary>Adds a row to its (consumer, tenant) tally for event 2013; the latest end of back-off wins.</summary>
    private static void Tally(
        ref List<(string Consumer, string Tenant, int Count, DateTimeOffset Until)>? tallies,
        string consumer,
        string tenant,
        DateTimeOffset until)
    {
        tallies ??= [];
        for (var i = 0; i < tallies.Count; i++)
        {
            var (byConsumer, byTenant, count, latest) = tallies[i];
            if (string.Equals(byConsumer, consumer, StringComparison.Ordinal)
                && string.Equals(byTenant, tenant, StringComparison.Ordinal))
            {
                tallies[i] = (byConsumer, byTenant, count + 1, until > latest ? until : latest);
                return;
            }
        }

        tallies.Add((consumer, tenant, 1, until));
    }

    /// <summary>Reads the row back and runs it through the pipeline's guarded retry.</summary>
    /// <exception cref="InboxPayloadException">
    /// The consumer is not registered for the row's type, the type does not resolve, or the payload
    /// cannot be read: permanent, and no attempt runs.
    /// </exception>
    private ValueTask<RetryOutcome> RetryAsync(
        InboxMessage message, Guid claimToken, CancellationToken cancellationToken)
    {
        // Registry lookup first: an unregistered consumer is permanent by construction. The key is
        // the PAIR (#187): one handler may handle several message types, each with its own invoker.
        var eventType = ResolveEventType(message);
        var local = false;
        if (!registry.TryGetInvoker(message.ConsumerType, eventType, out var entry))
        {
            // A local consumer's row (#188 D7) has the same shape: its message type and its name.
            local = localConsumers.TryGetConsumer(message.ConsumerType, eventType, out entry);
            if (!local)
            {
                throw new InboxPayloadException(
                    $"Unknown consumer type '{message.ConsumerType}' for '{eventType.FullName}'. It is " +
                    "absent from the handler registry and from the local consumers, so no number of " +
                    "retries will resolve it. Register the handler, then requeue.");
            }
        }

        var evt = Deserialize(message, eventType, local);
        var context = BuildContext(message, eventType);

        return pipeline.RetryAsync(message, entry, evt, context, claimToken, storeKey, instance, cancellationToken);
    }

    /// <summary>Deserializes the payload, treating every failure as permanent.</summary>
    /// <remarks>
    /// Deserialization is CPU-bound over an in-memory string: it has no transient failure mode, so
    /// a payload that cannot be read is a poison message, never a reason to spend the retry budget.
    /// </remarks>
    /// <param name="message">The row.</param>
    /// <param name="eventType">Its resolved type.</param>
    /// <param name="local">A local message, which need not be an <see cref="IIntegrationEvent"/>.</param>
    private object Deserialize(InboxMessage message, Type eventType, bool local)
    {
        object? deserialized;

        try
        {
            // The row stores the body as UTF-8 JSON text (application/json).
            deserialized = serializer.Deserialize(Encoding.UTF8.GetBytes(message.Payload), eventType);
        }
        catch (Exception ex)
        {
            throw new InboxPayloadException(
                $"Payload for EventType '{message.EventType}' could not be deserialized. " +
                "The persisted row must change before this can succeed.",
                ex);
        }

        return deserialized is IIntegrationEvent || (local && deserialized is not null)
            ? deserialized
            : throw new InboxPayloadException(
                $"Payload for EventType '{message.EventType}' deserialized to null or to a type " +
                "that is not an IIntegrationEvent. The persisted row must change before this " +
                "can succeed.");
    }

    /// <summary>
    /// Resolves the row's local message type. The row records it by assembly-qualified name; a name
    /// that no longer resolves is as permanent as an unreadable payload.
    /// </summary>
    private static Type ResolveEventType(InboxMessage message)
        => RowEventTypes.Resolve(message.EventType)
            ?? throw new InboxPayloadException(
                $"EventType '{message.EventType}' does not resolve to a type in this process. " +
                "The persisted row must change before this can succeed.");

    /// <summary>
    /// Rebuilds the handler's <see cref="MessageContext"/> from the row: what the first delivery saw,
    /// source and headers included (#188). The contract name and version come from the registry.
    /// </summary>
    private MessageContext BuildContext(InboxMessage message, Type eventType)
    {
        contracts.TryGetContract(eventType, out var contract);

        return new MessageContext
        {
            MessageId = message.MessageId,
            ContractName = contract?.Name ?? string.Empty,
            ContractVersion = contract?.Version ?? 0,
            Source = message.Source ?? string.Empty,
            OccurredOnUtc = message.OccurredOnUtc,
            TenantId = message.TenantId,
            // The column holds a GUID only; a correlation that is not one travels in the headers the
            // row kept, so the retry carries what the first delivery carried (#206 for the outbox).
            CorrelationId = message.CorrelationId?.Value.ToString()
                ?? message.Headers?.GetValueOrDefault(MessageHeaders.CorrelationId),
            CausationId = message.CausationId?.Value.ToString(),
            Headers = message.Headers ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private InboxOutcome BuildTransientOutcome(
        InboxMessage message, InboxMessageKey key, Exception ex)
    {
        var nextRetryCount = message.RetryCount + 1;
        var error = policy.Truncate(ex.Message);

        if (policy.IsExhausted(nextRetryCount))
        {
            InboxProcessorLogs.RetriesExhausted(
                logger, ex, message.MessageId.Value, message.ConsumerType, policy.MaxRetries);

            return InboxOutcome.DeadLetter(key, nextRetryCount, error);
        }

        var nextRetryAtUtc = policy.NextAttemptAt(nextRetryCount);

        InboxProcessorLogs.TransientFailure(
            logger, ex, message.MessageId.Value, message.ConsumerType,
            nextRetryCount, policy.MaxRetries, nextRetryAtUtc);

        return InboxOutcome.Retry(key, nextRetryCount, nextRetryAtUtc, error);
    }

    /// <summary>Persists deferred outcomes on an independent short timeout.</summary>
    /// <remarks>
    /// Deliberately not linked to the caller's token. Had a shutdown cancelled this write, every
    /// lease in the batch would stay held until expiry. A brief flush window on shutdown is
    /// strictly cheaper.
    /// </remarks>
    /// <returns><see langword="true"/> when every outcome was written.</returns>
    private async ValueTask<bool> SettleAsync(Guid claimToken, List<InboxOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return true;
        }

        using var settleCts = new CancellationTokenSource(options.OutcomeFlushTimeout);

        try
        {
            var written = await store
                .ApplyOutcomesAsync(claimToken, outcomes, settleCts.Token)
                .ConfigureAwait(false);

            if (written < outcomes.Count)
            {
                // Silent success is forbidden: fewer rows than outcomes means those rows no
                // longer carry this batch's token — their claim was lost, and nothing was counted.
                InboxProcessorLogs.PartialSettlement(logger, written, outcomes.Count);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: leases expire on their own and the rows are re-claimed.
            // Letting this escape would take down the loop over a condition the inbox is designed
            // to survive.
            InboxProcessorLogs.SettlementFailed(logger, ex, outcomes.Count);
            return false;
        }
    }

    private static InboxBatchResult Summarize(
        List<InboxOutcome> deferred,
        int claimedCount,
        int processed,
        int leasesLost,
        int tenantDeferred,
        InboxBatchAbortReason abortReason)
    {
        // A back-off's deferral is settled as a Retry with its attempt count unchanged: no attempt was
        // counted, so it is reported with the released rows.
        var retried = -tenantDeferred;
        var deadLettered = 0;
        var released = tenantDeferred;

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
}
