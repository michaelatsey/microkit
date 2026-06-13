namespace MicroKit.Messaging;

/// <summary>
/// Handles a specific type of integration event received through the messaging
/// infrastructure.
/// </summary>
/// <typeparam name="T">The type of integration event this handler processes.
/// Must implement <see cref="IIntegrationEvent"/>.</typeparam>
/// <remarks>
/// Implementations are invoked by the inbox processor after idempotency deduplication.
/// Each handler type gets its own row in the inbox keyed by
/// <c>(MessageId, ConsumerType)</c>, so multiple handlers for the same event type
/// are invoked independently and do not interfere with each other.
/// <para>
/// Register handlers as scoped or transient services. Singleton handlers are forbidden
/// unless they contain no mutable state.
/// </para>
/// </remarks>
public interface IMessageHandler<in T>
    where T : IIntegrationEvent
{
    /// <summary>
    /// Processes the integration event.
    /// </summary>
    /// <param name="evt">The event to handle. Never <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when handling is finished.</returns>
    ValueTask HandleAsync(T evt, CancellationToken ct = default);
}
