namespace MicroKit.Messaging.Processing;

/// <summary>
/// Dispatches one outbox row to its endpoint. Internal since #188: Core ships the only two
/// implementations, and a provider implements <see cref="IMessageTransport"/> instead.
/// </summary>
/// <remarks>
/// <para>
/// <b>One implementation per endpoint, resolved by the row's <see cref="OutboxMessage.Endpoint"/>,
/// never by its payload's CLR type.</b> <c>TransportOutboxDispatcher</c> serves a transport row
/// (<see langword="null"/> endpoint): it builds a <see cref="MessageEnvelope"/> from the columns and
/// hands it to <see cref="IMessageTransport"/>, without deserializing the payload.
/// <c>LocalOutboxDispatcher</c> serves a local row (<see cref="OutboxMessage.LocalEndpoint"/>): it
/// delivers the message through the receive pipeline, once per local consumer.
/// </para>
/// <para>
/// Scoped, keyed by <see cref="OutboxDispatchers"/>, and resolved from the message's own execution
/// scope by <c>OutboxProcessor</c>.
/// </para>
/// </remarks>
internal interface IOutboxDispatcher
{
    /// <summary>Dispatches one outbox row.</summary>
    /// <param name="message">The claimed row.</param>
    /// <param name="ct">The host's token; its cancellation means shutdown.</param>
    /// <returns>A task that completes once the endpoint has the message.</returns>
    /// <remarks>
    /// <see cref="OutboxPayloadException"/> dead-letters the row,
    /// <see cref="OutboxTransportUnavailableException"/> defers this endpoint's rows to the end of its
    /// back-off while the other endpoint's are still dispatched,
    /// <see cref="OutboxConfigurationException"/> stops the loop, and any other exception is a
    /// retry.
    /// </remarks>
    ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default);
}

/// <summary>The keys the two <see cref="IOutboxDispatcher"/> implementations are registered under.</summary>
internal static class OutboxDispatchers
{
    /// <summary>The key of <c>TransportOutboxDispatcher</c>, registered with every store.</summary>
    public const string Transport = "microkit.outbox.transport";

    /// <summary>The key of <c>LocalOutboxDispatcher</c>, registered with the store engine.</summary>
    public const string Local = "microkit.outbox.local";

    /// <summary>The key serving <paramref name="message"/>, or null when its endpoint cannot be routed.</summary>
    /// <param name="message">The row.</param>
    /// <returns>The dispatcher key, or <see langword="null"/>.</returns>
    public static string? KeyOf(OutboxMessage message) => message.Endpoint switch
    {
        null => Transport,
        OutboxMessage.LocalEndpoint => Local,
        _ => null,
    };
}
