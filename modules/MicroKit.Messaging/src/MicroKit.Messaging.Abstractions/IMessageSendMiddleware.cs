namespace MicroKit.Messaging;

/// <summary>A step of the send pipeline, run on every publication through the bus.</summary>
/// <remarks>
/// <para>
/// Middleware runs in registration order, before the message is serialized. Each step calls
/// <c>proceed</c> to continue. Not calling it stops the publication before any transport, and
/// <c>PublishAsync</c> then throws <see cref="IntegrationEventPublishException"/>: a publication
/// either reaches the transport or fails, it never succeeds silently.
/// </para>
/// <para>
/// A step may add headers through <see cref="MessageSendContext.Headers"/>. The reserved headers
/// (<c>tenantid</c>, <c>correlationid</c>, <c>causationid</c>, <c>traceparent</c>) are written by
/// the bus <b>after</b> the last step and overwrite anything a step set: middleware cannot forge a
/// tenant.
/// </para>
/// </remarks>
public interface IMessageSendMiddleware
{
    /// <summary>Runs this step.</summary>
    /// <param name="context">The publication in flight.</param>
    /// <param name="proceed">The rest of the pipeline.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when this step and the rest are done.</returns>
    ValueTask InvokeAsync(
        MessageSendContext context,
        Func<ValueTask> proceed,
        CancellationToken ct = default);
}
