namespace MicroKit.Messaging.Local;

using System.Runtime.ExceptionServices;
using MicroKit.Messaging.Bus;

/// <summary>
/// Dispatches a local outbox row (<see cref="OutboxMessage.LocalEndpoint"/>): delivers it through the
/// receive pipeline, once per local consumer, each guarded by its own inbox row (#188 D7).
/// </summary>
/// <remarks>
/// <para>
/// Returning marks the row <see cref="OutboxMessageStatus.Published"/>: every consumer either
/// committed its row with its work, was already done, or had its failure recorded on its row —
/// from which the store's inbox loop retries it. Nothing is ever handed to
/// <see cref="IMessageTransport"/>, so this works with any transport or none.
/// </para>
/// <para>
/// A verdict the pipeline could not record becomes the outbox's own: a payload no process can read
/// dead-letters the row; a dependency outage defers the batch's local rows to the end of the local
/// endpoint's back-off, with no retry spent, while its transport rows go out; a failure no
/// inbox row could hold retries the row — the consumers already done skip on their rows.
/// </para>
/// </remarks>
internal sealed class LocalOutboxDispatcher(MessageReceivePipeline pipeline) : IOutboxDispatcher
{
    /// <inheritdoc />
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var outcome = await pipeline.DeliverLocalAsync(message, consumerType: null, ct).ConfigureAwait(false);

        switch (outcome.Verdict)
        {
            case DeliveryVerdict.Succeeded:
                return;

            case DeliveryVerdict.Permanent:
                throw new OutboxPayloadException(
                    $"Local message {message.Id} cannot be delivered: {outcome.Reason}", outcome.Error!);

            case DeliveryVerdict.DependencyUnavailable:
                throw new OutboxTransportUnavailableException(
                    $"Local message {message.Id}: a consumer's dependency is unavailable.", outcome.Error!);

            case DeliveryVerdict.Unsettled:
                if (outcome.Error is OperationCanceledException or ObjectDisposedException)
                {
                    ExceptionDispatchInfo.Capture(outcome.Error).Throw();
                }

                throw new OperationCanceledException($"Local message {message.Id} was left unsettled by a stop.", outcome.Error, ct);

            default:
                throw new LocalDeliveryException(
                    $"Local message {message.Id}: a consumer's failure could not be recorded. {outcome.Reason}",
                    outcome.Error);
        }
    }
}

/// <summary>A local delivery whose failure no inbox row holds: the outbox row is retried.</summary>
internal sealed class LocalDeliveryException(string message, Exception? innerException)
    : Exception(message, innerException);
