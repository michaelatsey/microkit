namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Generic EF Core write-side repository base implementing <see cref="IRepository{TAggregate}"/>.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <typeparam name="TContext">
/// The application <see cref="DbContext"/> type.
/// </typeparam>
/// <remarks>
/// <para>
/// Extend this class for typed repositories:
/// <code>
/// public sealed class EfUserRepository(AppDbContext ctx, IUnitOfWork uow)
///     : EfRepository&lt;User, AppDbContext&gt;(ctx, uow), IUserRepository
/// {
///     public async ValueTask&lt;User?&gt; FindByEmailAsync(Email email, CancellationToken ct = default)
///         => await Context.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
///                .ConfigureAwait(false);
/// }
/// </code>
/// </para>
/// <para>
/// <see cref="CommitAsync"/> delegates to the injected <see cref="IUnitOfWork"/> — it does NOT
/// call <c>SaveChangesAsync</c> directly. This ensures exception translation logic lives in
/// exactly one place: <see cref="EfUnitOfWork{TContext}"/>.
/// </para>
/// </remarks>
public class EfRepository<TAggregate, TContext>(TContext context, IUnitOfWork uow)
    : IRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
    where TContext : DbContext
{
    /// <summary>
    /// The underlying <typeparamref name="TContext"/> DbContext.
    /// Available to typed subclasses for aggregate-specific query methods.
    /// </summary>
    protected TContext Context { get; } = context;

    /// <summary>
    /// Stages a new aggregate for insertion. Changes are not persisted until
    /// <see cref="CommitAsync"/> is called.
    /// </summary>
    /// <param name="aggregate">The aggregate to insert. Must not already exist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await Context.Set<TAggregate>().AddAsync(aggregate, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stages an existing aggregate for update. Changes are not persisted until
    /// <see cref="CommitAsync"/> is called.
    /// </summary>
    /// <param name="aggregate">The aggregate to update. Must already be tracked or detached.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public ValueTask UpdateAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        Context.Set<TAggregate>().Update(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stages an aggregate for deletion. Changes are not persisted until
    /// <see cref="CommitAsync"/> is called.
    /// </summary>
    /// <param name="aggregate">The aggregate to delete.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        Context.Set<TAggregate>().Remove(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Commits all pending changes to the underlying store by delegating to the injected
    /// <see cref="IUnitOfWork"/>. Exception translation is handled by
    /// <see cref="EfUnitOfWork{TContext}"/>.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">
    /// Thrown when the underlying provider fails to commit.
    /// </exception>
    public ValueTask CommitAsync(CancellationToken ct = default)
        => uow.CommitAsync(ct);

    /// <summary>
    /// Finds an aggregate by its primary key with EF Core change tracking enabled —
    /// required when the aggregate will be mutated after loading.
    /// </summary>
    /// <param name="keyValues">
    /// The primary key values. Pass a single-element array for simple PKs;
    /// multiple elements for composite PKs, in the same order as the key definition.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The matching aggregate, or <see langword="null"/> if not found.</returns>
    /// <remarks>
    /// Typed subclasses should expose a strongly-typed overload (e.g.,
    /// <c>FindAsync(UserId id, CancellationToken ct)</c>) that delegates here or uses a LINQ query.
    /// </remarks>
    protected async ValueTask<TAggregate?> FindByKeyAsync(
        object[] keyValues,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        return await Context.Set<TAggregate>().FindAsync(keyValues, ct).ConfigureAwait(false);
    }
}
