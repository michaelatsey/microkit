namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Executes a database operation inside an explicit database transaction.
/// Begin, Commit, and Rollback are managed internally by the implementation.
/// </summary>
/// <remarks>
/// Consumed by <c>TransactionBehavior</c> in <c>MicroKit.MediatR.Behaviors</c>, which wraps
/// <c>ICommand</c> handlers automatically. The <c>ExecuteAsync&lt;TState&gt;</c> pattern
/// is closure-free and compatible with EF Core's execution-strategy retry mechanism.
/// </remarks>
public interface ITransactionalContext
{
    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction.
    /// The implementation begins a transaction, invokes the operation, commits on success,
    /// and rolls back on failure. Transient-failure retry is handled transparently when
    /// supported by the provider (e.g., Npgsql, SQL Server execution strategies).
    /// </summary>
    /// <typeparam name="TState">
    /// The type of caller-owned state threaded through to <paramref name="operation"/>.
    /// Using a state carrier avoids lambda closures and heap allocations on the hot path.
    /// </typeparam>
    /// <param name="operation">The work to execute inside the transaction.</param>
    /// <param name="state">State passed to <paramref name="operation"/> on each attempt.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    Task ExecuteAsync<TState>(
        Func<TState, CancellationToken, Task> operation,
        TState state,
        CancellationToken ct = default);
}
