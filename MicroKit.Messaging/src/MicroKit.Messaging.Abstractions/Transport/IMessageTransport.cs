namespace MicroKit.Messaging.Abstractions.Transport;

/// <summary>
/// Réprésente le transport de messages pour l'envoi et la réception de messages via le système de messagerie.
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// Sends the asynchronous.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="messageType">Type of the message.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="properties">The properties.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task SendAsync(
        string destination,
        string messageType,
        string payload,
        string? correlationId = null,
        Dictionary<string, string>? properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the batch asynchronous.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task SendBatchAsync(
        IEnumerable<TransportMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the listening asynchronous.
    /// </summary>
    /// <param name="queueName">Name of the queue.</param>
    /// <param name="consumerId">The consumer identifier.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task StartListeningAsync(
        string queueName,
        string consumerId,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the listening asynchronous.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task StopListeningAsync(
        CancellationToken cancellationToken = default);
}
