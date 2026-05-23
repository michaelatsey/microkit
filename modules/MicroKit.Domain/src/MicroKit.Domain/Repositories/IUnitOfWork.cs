using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Repositories;

/// <summary>
/// Represents a transaction boundary for domain operations.
/// Ensures consistency across multiple repository operations.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Commits all changes made within this unit of work.
    /// Should publish domain events after successful persistence.
    /// </summary>
    void Commit();

    /// <summary>
    /// Rolls back all changes made within this unit of work.
    /// Returns aggregates to their previous state.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Gets a repository instance for the specified aggregate type.
    /// Repository participates in this unit of work's transaction.
    /// </summary>
    IRepository<TEntity, TId> Repository<TEntity, TId>()
        where TEntity : AggregateRoot<TId>
        where TId : IEntityId;
}