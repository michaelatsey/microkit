namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Defines the commit boundary for aggregate persistence.
/// </summary>
/// <remarks>
/// Inject <see cref="IUnitOfWork"/> in command handlers only; call
/// <see cref="CommitAsync"/> exactly once per command handler invocation,
/// after all staging operations (<c>AddAsync</c>, <c>UpdateAsync</c>, <c>DeleteAsync</c>)
/// have been performed.
/// <para>
/// This interface was moved from <c>MicroKit.Domain</c> to
/// <c>MicroKit.Persistence.Abstractions</c> (ADR-001) because committing is an
/// infrastructure concern — the domain layer has no knowledge of when or how
/// changes are persisted.
/// </para>
/// For cross-aggregate transactional scenarios, use
/// <see cref="ITransactionalContext"/> instead.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all pending changes accumulated since the last commit.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">
    /// Thrown when the underlying provider fails to commit (connection failure,
    /// constraint violation, or concurrency conflict).
    /// </exception>
    ValueTask CommitAsync(CancellationToken ct = default);
}
