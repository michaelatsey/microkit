using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>
/// Réprésente le référentiel de la boîte de réception (Inbox) pour l'accès et la gestion des messages reçus.
/// </summary>
public interface IInboxMessageRepository
{
    Task<bool> ExistsAsync(
        string tenantId,
        string messageId,
        CancellationToken cancellationToken = default);

    Task AddAsync(InboxMessage message, CancellationToken cancellationToken = default);

    Task<InboxMessage?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    
    Task<InboxMessage?> GetAsync(
        string tenantId,
        string messageId,
        CancellationToken cancellationToken = default);

}
