namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="ITransactionalUnitOfWork"/>.
/// Wraps a <typeparamref name="TContext"/> <see cref="DbContext"/> and translates provider
/// exceptions into <see cref="PersistenceException"/>.
/// </summary>
/// <typeparam name="TContext">
/// The application <see cref="DbContext"/> type. Injected as a scoped service.
/// </typeparam>
/// <remarks>
/// Register via
/// <see cref="PersistenceServiceCollectionExtensions.AddUnitOfWork{TContext}"/>, which creates
/// a single scoped instance and binds it to <see cref="IUnitOfWork"/>,
/// <see cref="ITransactionalContext"/>, and <see cref="ITransactionalUnitOfWork"/>.
/// </remarks>
public sealed class EfUnitOfWork<TContext>(TContext context)
    : ITransactionalUnitOfWork
    where TContext : DbContext
{
    /// <summary>
    /// Flushes all pending EF Core change-tracker entries to the database via
    /// <c>SaveChangesAsync</c>. Translates provider failures into
    /// <see cref="PersistenceException"/>.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">
    /// Thrown on an optimistic concurrency conflict (<see cref="DbUpdateConcurrencyException"/>)
    /// or any other database write failure (<see cref="DbUpdateException"/>).
    /// The original provider exception is preserved as <see cref="Exception.InnerException"/>.
    /// </exception>
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceException("Optimistic concurrency conflict during commit.", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new PersistenceException("Database error during commit.", ex);
        }
    }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction using the
    /// provider's execution strategy. Commits on success, rolls back on failure.
    /// Transient failures are retried automatically when the provider supports it.
    /// </summary>
    /// <typeparam name="TState">
    /// Caller-owned state threaded through to <paramref name="operation"/>.
    /// Avoids lambda closures on the hot path.
    /// </typeparam>
    /// <param name="operation">The work to execute inside the transaction.</param>
    /// <param name="state">State passed to <paramref name="operation"/> on each attempt.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async Task ExecuteAsync<TState>(
        Func<TState, CancellationToken, Task> operation,
        TState state,
        CancellationToken ct = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await operation(state, ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);
    }
}
