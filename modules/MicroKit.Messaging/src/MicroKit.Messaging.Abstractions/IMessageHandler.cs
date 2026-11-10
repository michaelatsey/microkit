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
    /// <remarks>
    /// <para>
    /// <b>What you throw decides what the engine does with the row</b>, so the choice is part of
    /// this contract rather than an implementation detail. The default — letting an untyped
    /// exception propagate — is a transient failure: the row retries with full-jitter back-off
    /// until <c>MaxRetries</c>, then dead-letters. The two typed verdicts below exist for the cases
    /// where that default is wrong.
    /// </para>
    /// <para>
    /// Throwing nothing means success. If your handler commits a unit of work through the scope's
    /// own <c>DbContext</c>, the processed mark commits inside that same transaction; a handler that
    /// commits nothing at all is detected and reported, never degraded silently.
    /// </para>
    /// </remarks>
    /// <exception cref="InboxDependencyUnavailableException">
    /// A dependency the whole batch relies on is unreachable, so handling the next row is certain
    /// to fail too. The processor abandons the batch and releases every unattempted row
    /// <b>without consuming a retry</b>. Throw this for a downstream service that is down, a
    /// refused connection or rejected authentication — never for one message failing while the
    /// dependency is healthy.
    /// </exception>
    /// <exception cref="Spi.InboxPayloadException">
    /// The payload is permanently unprocessable — it parses but violates the event contract. The
    /// row dead-letters on <b>first sight</b>, consuming no retries. Never throw this for a
    /// timeout, a refused connection, an HTTP 503, a database timeout or a deadlock: guessing
    /// permanence wrongly loses the message. It lives in <c>MicroKit.Messaging.Spi</c> because the
    /// engine is its primary raiser; a handler that wants this verdict needs that
    /// <see langword="using"/>.
    /// </exception>
    ValueTask HandleAsync(T evt, CancellationToken ct = default);
}
