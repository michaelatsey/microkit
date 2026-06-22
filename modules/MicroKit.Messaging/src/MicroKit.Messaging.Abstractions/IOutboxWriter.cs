namespace MicroKit.Messaging;

/// <summary>
/// Write-only access to the transactional outbox for use within domain command handlers.
/// </summary>
/// <remarks>
/// <see cref="IOutboxWriter"/> is intentionally minimal — it exposes only
/// <see cref="AddAsync"/>. Domain handlers must never have access to
/// <see cref="IOutboxProcessorStore"/> methods such as <c>GetPendingAsync</c> or
/// <c>DeadLetterAsync</c>, which are reserved for the background processor.
/// <para>
/// The EF Core implementation (<c>EfOutboxStore</c>) resolves from the same
/// <c>DbContext</c> as the domain aggregate, ensuring the outbox write and the
/// business data write share the same database transaction.
/// </para>
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>
    /// Adds an outbox message within the current domain transaction.
    /// The message is not persisted until the enclosing transaction commits.
    /// </summary>
    /// <param name="message">The outbox message to persist.
    /// <see cref="OutboxMessage.TenantId"/> is optional. Null in single-tenant deployments.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the message has been
    /// added to the unit of work.</returns>
    ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Adds multiple outbox messages within the current domain transaction in a single operation.
    /// No messages are persisted until the enclosing transaction commits.
    /// </summary>
    /// <param name="messages">The outbox messages to persist. An empty list is a no-op.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all messages have been
    /// added to the unit of work.</returns>
    ValueTask AddBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default);
}
