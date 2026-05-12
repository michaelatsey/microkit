namespace MicroKit.Data.Abstractions;

/// <summary>Provides a transactional execution scope for database operations.</summary>
public interface ITransactionalContext
{
    /// <summary>Executes the given operation inside a database transaction.</summary>
    /// <param name="operation">The async operation to run within the transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
}
