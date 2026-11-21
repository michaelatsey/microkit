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
/// dependency outage abandons the batch and releases the remainder with no attempt counted; a lost
/// claim writes nothing; a host stop releases the remainder with no attempt counted; everything
/// else is transient, retried with full-jitter back-off until <see cref="InboxProcessorOptions.MaxRetries"/>.
/// Failures are settled in one call at the end of the batch, each conditioned on the claim.
/// </para>
/// </remarks>
/// <param name="store">
/// Claim and settlement of failures. Batch-scoped by design (ADR-MSG-002 shared-DB cross-tenant
/// reservation): NOT resolved from a per-message scope. It is the store this processor drains,
/// resolved by the store's key from its loop's scope (#188); a dynamic store set is #191's.
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
    MessageStoreKey storeKey)
    : IInboxProcessor
{
    /// <inheritdoc />
    public async ValueTask<InboxBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var claim = await store
            .ClaimBatchAsync(batchSize, options.LeaseDuration, cancellationToken)
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
        var index = 0;

        for (; index < messages.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                abortReason = InboxBatchAbortReason.Cancelled;
                InboxProcessorLogs.BatchCancelled(logger, messages.Count - index);
                break;
            }

            var message = messages[index];
            var key = new InboxMessageKey(message.MessageId, message.ConsumerType);

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
                InboxProcessorLogs.ServiceUnresolvable(logger, ex, messages.Count - index);
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
                InboxProcessorLogs.BatchCancelled(logger, messages.Count - index);
                break;
            }

            switch (outcome.Result)
            {
                case RetryResult.Processed:
                    processed++;
                    continue;

                case RetryResult.LeaseLost:
                    leasesLost++;
                    continue;

                default:
                    break;
            }

            switch (outcome.Verdict)
            {
                case DeliveryVerdict.DependencyUnavailable:
                    abortReason = InboxBatchAbortReason.DependencyUnavailable;
                    InboxProcessorLogs.DependencyUnavailable(logger, outcome.Error!, messages.Count - index);
                    break;

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

            // Only the dependency-unavailable arm reaches here: the batch is abandoned.
            break;
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
            throw configurationFault;
        }

        return Summarize(deferred, messages.Count, processed, leasesLost, abortReason);
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

        return pipeline.RetryAsync(message, entry, evt, context, claimToken, storeKey, cancellationToken);
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
    private async ValueTask SettleAsync(Guid claimToken, List<InboxOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return;
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
            }
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: leases expire on their own and the rows are re-claimed.
            // Letting this escape would take down the loop over a condition the inbox is designed
            // to survive.
            InboxProcessorLogs.SettlementFailed(logger, ex, outcomes.Count);
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
}
