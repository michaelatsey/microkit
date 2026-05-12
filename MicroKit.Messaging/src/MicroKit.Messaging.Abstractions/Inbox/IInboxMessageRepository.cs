using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>Persistence contract for reading and writing raw inbox message entries.</summary>
public interface IInboxMessageRepository
{
    /// <summary>Returns <see langword="true"/> if a message with the given ID already exists for the tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="messageId">The message identifier to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsAsync(
        string tenantId,
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a new inbox message.</summary>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(InboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>Retrieves an inbox message by its primary identifier.</summary>
    /// <param name="id">The primary key of the inbox message record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching <see cref="InboxMessage"/>, or <see langword="null"/> if not found.</returns>
    Task<InboxMessage?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a message by tenant and message ID.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching <see cref="InboxMessage"/>, or <see langword="null"/> if not found.</returns>
    Task<InboxMessage?> GetAsync(
        string tenantId,
        string messageId,
        CancellationToken cancellationToken = default);

}
