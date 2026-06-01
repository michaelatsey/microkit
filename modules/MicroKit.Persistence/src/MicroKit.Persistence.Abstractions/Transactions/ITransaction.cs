namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Represents an active database transaction.
/// </summary>
/// <remarks>
/// Implementations wrap the underlying provider transaction
/// (e.g., <c>IDbContextTransaction</c> in EF Core).
/// <see cref="IAsyncDisposable"/> ensures rollback on unhandled exceptions
/// when used with <c>await using</c> — disposing an uncommitted transaction performs a rollback.
/// </remarks>
public interface ITransaction : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier of this transaction.
    /// Useful for correlation in distributed tracing and structured logging.
    /// </summary>
    Guid TransactionId { get; }
}
