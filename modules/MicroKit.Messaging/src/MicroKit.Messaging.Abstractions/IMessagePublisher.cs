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
    /// Thrown when no transport is registered. Never fakes success in this case.
    /// </exception>
    ValueTask PublishAsync<T>(T evt, CancellationToken ct = default)
        where T : IIntegrationEvent;
}
