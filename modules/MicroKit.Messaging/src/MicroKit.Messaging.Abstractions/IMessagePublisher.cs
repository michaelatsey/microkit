namespace MicroKit.Messaging;

/// <summary>
/// Publishes integration events to the configured transport
/// (in-process, outbox, or broker).
/// </summary>
/// <remarks>
/// The v1 default implementation is <c>InProcessMessagePublisher</c> (in
/// <c>MicroKit.Messaging</c> Core), which routes events to registered
/// <see cref="IMessageHandler{T}"/> instances within the same process.
/// Broker implementations are v2 opt-in.
/// <para>
/// Implementations must never silently succeed when no transport is registered —
/// throw <see cref="InvalidOperationException"/> instead.
/// </para>
/// </remarks>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes an integration event to the configured transport.
    /// </summary>
    /// <typeparam name="T">The type of the integration event.</typeparam>
    /// <param name="evt">The event to publish. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the event has been
    /// accepted by the transport layer.</returns>
    /// <exception cref="InvalidOperationException">
    /// <b>Required of implementations</b> that can find themselves without a usable transport:
    /// throw rather than return, so a lost message is never reported as a delivered one. The
    /// v1 <c>InProcessMessagePublisher</c> cannot reach that state — its transport is its own
    /// inbox writer — so it never raises this; an event with no registered consumer is a valid
    /// configuration and is logged, not thrown.
    /// </exception>
    ValueTask PublishAsync<T>(T evt, CancellationToken ct = default)
        where T : IIntegrationEvent;
}
