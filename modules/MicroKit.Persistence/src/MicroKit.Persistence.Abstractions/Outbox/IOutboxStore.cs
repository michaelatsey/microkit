namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Enqueues MediatR notifications to the transactional outbox via the change tracker.
/// </summary>
/// <remarks>
/// <see cref="Add"/> is synchronous and performs no I/O — it only enqueues an entry in
/// the EF Core change tracker. Persistence happens atomically alongside all other
/// pending aggregate changes during the enclosing <see cref="IUnitOfWork.CommitAsync"/> call.
/// </remarks>
public interface IOutboxStore
{
    /// <summary>
    /// Enqueues <paramref name="notification"/> for persistence in the outbox.
    /// No <c>SaveChanges</c> is called — the entry is committed atomically with all other
    /// change-tracked entities when <see cref="IUnitOfWork.CommitAsync"/> is invoked.
    /// </summary>
    /// <param name="notification">The notification to persist in the outbox.</param>
    void Add(INotification notification);
}
