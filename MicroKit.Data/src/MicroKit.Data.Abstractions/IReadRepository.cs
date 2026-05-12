namespace MicroKit.Data.Abstractions;

/// <summary>
/// Read-only repository contract for querying entities of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public interface IReadRepository<T> where T : class
{
    /// <summary>
    /// Finds an entity by its primary key.
    /// </summary>
    /// <param name="id">The primary key value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The entity if found; otherwise <see langword="null"/></returns>
    Task<T?> FindByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all entities.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all entities matching the given predicate.
    /// </summary>
    /// <param name="predicate">Filter expression</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<IReadOnlyCollection<T>> FindAsync(
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether any entity matches the given predicate.
    /// </summary>
    Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);
}
