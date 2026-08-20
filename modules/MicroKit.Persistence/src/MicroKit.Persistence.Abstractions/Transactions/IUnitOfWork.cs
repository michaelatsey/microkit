namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Defines the change-set boundary for aggregate persistence: a unit of work is either
/// committed or discarded.
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

    /// <summary>
    /// Abandons every pending change accumulated since the last commit, without writing them.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="CommitAsync"/>: a unit of work either commits its pending
    /// change set or discards it. Call this at any command boundary that does <em>not</em> commit —
    /// business failure and thrown exception alike. A database transaction rollback does not reset
    /// the pending change set, so the exception path needs the discard exactly as much as the
    /// failure path.
    /// <para>
    /// Synchronous by design — no implementation performs I/O. EF Core drops change-tracker
    /// references (<c>ChangeTracker.Clear()</c>); a provider that accumulates no pending change
    /// set (Dapper, raw SQL) satisfies this as a no-op.
    /// </para>
    /// <para>
    /// Discards the whole context's pending set, not one command's entities. Entity references held
    /// across the call become detached: the in-memory object graph is left intact, but the provider
    /// no longer tracks it, so re-saving such a reference later inserts a duplicate. Lazy and
    /// explicit navigation loads on a detached entity do <em>not</em> throw — they issue a fresh
    /// query, which inside a failing command means a round-trip on a transaction that is about to
    /// roll back.
    /// </para>
    /// <para>
    /// Called by <c>TransactionBehavior</c>, never by a command handler — a handler cannot know
    /// whether its scope holds one command or twenty. See ADR-005.
    /// </para>
    /// </remarks>
    void DiscardChanges();
}
