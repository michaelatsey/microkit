using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Serialization;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The standard <see cref="IEnvelopeReceiver"/>: hands an envelope arriving from a broker provider
/// to the receive pipeline, and turns the pipeline's verdict into what the transport must do.
/// </summary>
/// <remarks>
/// <para>
/// <b>One pipeline (#188 D6).</b> The envelope reaches its handlers through the same
/// <see cref="MessageReceivePipeline"/> as the in-memory transport's consume loop: the contract
/// resolved to this process's own type, each handler in its own scope, guarded by its inbox row when
/// the handler has a store. Nothing is queued for a later drain.
/// </para>
/// <para>
/// <b>The verdict maps one to one.</b> Succeeded → <see cref="ReceiveDisposition.Acknowledge"/>;
/// permanent → <see cref="ReceiveDisposition.DeadLetter"/>; transient →
/// <see cref="ReceiveDisposition.Redeliver"/>; dependency unavailable →
/// <see cref="ReceiveDisposition.PauseAndRedeliver"/>; a host stop →
/// <see cref="ReceiveDisposition.Unsettled"/>.
/// </para>
/// <para>
/// <b>JSON only.</b> An envelope whose content type is not JSON is dead-lettered before any handler:
/// a guarded handler's row records the body as JSON text, and the default serializer reads nothing
/// else.
/// </para>
/// </remarks>
internal sealed class EnvelopeReceiver(MessageReceivePipeline pipeline) : IEnvelopeReceiver
{
    /// <inheritdoc />
    public async ValueTask<EnvelopeReceiveResult> ReceiveAsync(
        MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!SystemTextJsonMessageSerializer.IsJson(envelope.DataContentType))
        {
            return EnvelopeReceiveResult.From(
                ReceiveDisposition.DeadLetter,
                $"Message '{envelope.Id}' ('{envelope.Type}') carries content type " +
                $"'{envelope.DataContentType}'; only application/json is received.");
        }

        var outcome = await pipeline.DeliverAsync(envelope, ct).ConfigureAwait(false);

        return outcome.Verdict switch
        {
            DeliveryVerdict.Succeeded => EnvelopeReceiveResult.Acknowledged,
            DeliveryVerdict.Permanent => EnvelopeReceiveResult.From(
                ReceiveDisposition.DeadLetter, outcome.Reason ?? outcome.Error?.Message, outcome.Error),
            DeliveryVerdict.Transient => EnvelopeReceiveResult.From(
                ReceiveDisposition.Redeliver, outcome.Reason ?? outcome.Error?.Message, outcome.Error),
            DeliveryVerdict.DependencyUnavailable => EnvelopeReceiveResult.From(
                ReceiveDisposition.PauseAndRedeliver, outcome.Reason ?? outcome.Error?.Message, outcome.Error),
            DeliveryVerdict.Unsettled => EnvelopeReceiveResult.From(
                ReceiveDisposition.Unsettled, "The host is stopping.", outcome.Error),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(DeliveryVerdict)} '{outcome.Verdict}'."),
        };
    }
}
