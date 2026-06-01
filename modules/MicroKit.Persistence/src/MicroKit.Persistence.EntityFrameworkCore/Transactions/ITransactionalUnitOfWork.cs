namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// EF Core composite that combines the Unit of Work commit boundary with explicit
/// database transaction management. Declared in <c>MicroKit.Persistence.EntityFrameworkCore</c>
/// — not in <c>MicroKit.Persistence.Abstractions</c> — per ADR-004.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EfUnitOfWork{TContext}"/> implements this interface.
/// </para>
/// <para>
/// Registered as a single scoped instance exposed through three interface pointers:
/// <see cref="IUnitOfWork"/>, <see cref="ITransactionalContext"/>, and
/// <see cref="ITransactionalUnitOfWork"/>. Use
/// <see cref="PersistenceServiceCollectionExtensions.AddUnitOfWork{TContext}"/> to register all
/// three in one call.
/// </para>
/// <para>
/// Handlers inject the narrowest interface they need:
/// command handlers inject <see cref="IUnitOfWork"/>;
/// the <c>TransactionBehavior</c> in <c>MicroKit.MediatR.Behaviors</c> injects
/// <see cref="ITransactionalContext"/>.
/// </para>
/// </remarks>
public interface ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext { }
