namespace MicroKit.Messaging;

/// <summary>
/// Handles a specific type of integration event received through the messaging
/// infrastructure.
/// </summary>
/// <typeparam name="T">The type of integration event this handler processes.
/// Must implement <see cref="IIntegrationEvent"/>.</typeparam>
/// <remarks>
/// <para>
/// <b>On the bus</b>, a handler runs inline on receipt, in its own message scope: each handler of
/// a contract gets a separate scope, so they cannot share a <c>DbContext</c> or interfere through
/// scoped state. The scope's execution context is filled from the envelope first — tenant,
/// correlation, and the incoming message as causation — so a message published from the handler
/// carries them on.
/// </para>
/// <para>
/// <b>Delivery is at-least-once, and without an inbox a handler that already succeeded may run
/// again.</b> When one handler of a message fails transiently, the whole message is redelivered,
/// and every handler of it runs again, including those that had succeeded. Handlers must be
/// idempotent. #188 adds the per-handler inbox guard that removes the rerun.
/// </para>
/// <para>
/// <b>On the transitional inbox path</b>, handlers are invoked by the inbox processor after
/// deduplication, one row per <c>(MessageId, ConsumerType)</c>.
/// </para>
/// <para>
/// Register handlers as scoped or transient services. Singleton handlers are forbidden unless they
/// contain no mutable state.
/// </para>
/// </remarks>
public interface IMessageHandler<in T>
    where T : IIntegrationEvent
{
    /// <summary>
    /// Processes the integration event.
    /// </summary>
    /// <param name="message">The event to handle. Never <see langword="null"/>.</param>
    /// <param name="context">
    /// The message's identity, contract, source, occurrence time, tenant, correlation, causation
    /// and headers.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when handling is finished.</returns>
    /// <remarks>
    /// <para>
    /// <b>What you throw decides what the engine does with the message</b>, so the choice is part
    /// of this contract rather than an implementation detail. The default — letting an untyped
    /// exception propagate — is a transient failure: the message is redelivered (on the inbox path,
    /// the row retries with full-jitter back-off until <c>MaxRetries</c>), then dead-letters. The
    /// two typed verdicts below exist for the cases where that default is wrong.
    /// </para>
    /// <para>
    /// Throwing nothing means success. On the inbox path, if your handler commits a unit of work
    /// through the scope's own <c>DbContext</c>, the processed mark commits inside that same
    /// transaction; a handler that commits nothing at all is detected and reported.
    /// </para>
    /// <para>
    /// Host shutdown is not a failure: a handler interrupted by it is left unsettled and the
    /// message is redelivered with its attempt count unchanged.
    /// </para>
    /// </remarks>
    /// <exception cref="InboxDependencyUnavailableException">
    /// A dependency every message relies on is unreachable, so handling the next one is certain to
    /// fail too. On the bus the endpoint pauses for a back-off and the message is redelivered
    /// <b>without counting an attempt</b>; on the inbox path the processor abandons the batch and
    /// releases every unattempted row without consuming a retry. Throw this for a downstream
    /// service that is down, a refused connection or rejected authentication — never for one
    /// message failing while the dependency is healthy.
    /// </exception>
    /// <exception cref="PermanentMessageException">
    /// The message is permanently unprocessable — it parses but violates the event contract. It
    /// dead-letters on <b>first sight</b>, consuming no retries. Never throw this for a timeout, a
    /// refused connection, an HTTP 503, a database timeout or a deadlock: guessing permanence
    /// wrongly loses the message.
    /// </exception>
    ValueTask HandleAsync(T message, MessageContext context, CancellationToken ct = default);
}
