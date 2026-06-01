namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Wraps an EF Core <see cref="IDbContextTransaction"/> as an <see cref="ITransaction"/>,
/// assigning a stable <see cref="TransactionId"/> for correlation and logging.
/// </summary>
public sealed class EfTransaction : ITransaction
{
    private readonly IDbContextTransaction _inner;

    internal EfTransaction(IDbContextTransaction inner)
    {
        _inner = inner;
        TransactionId = Guid.NewGuid();
    }

    /// <inheritdoc/>
    public Guid TransactionId { get; }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
        => await _inner.DisposeAsync().ConfigureAwait(false);
}
