namespace MicroKit.MediatR.Behaviors;

/// <summary>
/// Wraps <see cref="ICommand"/> and <see cref="ICommand{TResult}"/> handlers in a database
/// transaction (pipeline order <see cref="PipelineOrder.Transaction"/> = 700).
/// Queries, events, and any request that is not a command pass through without a transaction.
/// </summary>
/// <remarks>
/// <para>
/// Execution sequence for command requests:
/// <list type="number">
/// <item><description><see cref="ITransactionalContext.ExecuteAsync{TState,TResult}"/> opens a database transaction.</description></item>
/// <item><description>The next pipeline delegate (the command handler) executes and stages aggregate
/// changes in the EF Core change tracker.</description></item>
/// <item><description>On business success, <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> drains
/// the accumulated domain events and stages their side-effects (outbox rows) in the same change tracker.</description></item>
/// <item><description><see cref="IUnitOfWork.CommitAsync"/> flushes aggregates and outbox rows in a
/// single <c>SaveChangesAsync</c>. This MUST run <b>after</b> the dispatch — the rows staged by
/// step 3 are otherwise never written.</description></item>
/// <item><description><see cref="ITransactionalContext"/> then commits the underlying database transaction.</description></item>
/// <item><description>On any exception, the transaction is rolled back.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Two different <c>CommitAsync</c> calls — do not conflate them.</b>
/// <see cref="IUnitOfWork.CommitAsync"/> is the <em>flush</em>: it calls <c>SaveChangesAsync</c> and
/// is what actually writes rows. The database transaction commit is
/// <c>IDbContextTransaction.CommitAsync</c>, performed internally by
/// <see cref="ITransactionalContext"/>. This behavior calls the first; it never calls the second.
/// Committing the transaction without flushing commits an empty transaction and writes nothing.
/// </para>
/// <para>
/// A business failure (<c>Result.IsFailure</c>) dispatches nothing and flushes nothing: staged
/// changes are discarded when the scope ends.
/// </para>
/// <para>
/// The static lambda + <c>readonly struct</c> state-carrier pattern ensures zero heap allocation
/// per dispatch: no closure display class is allocated, and the JIT specializes
/// <c>ExecuteAsync&lt;TransactionHandlerState, TResponse&gt;</c> without boxing.
/// </para>
/// <para>
/// Requires <see cref="ITransactionalContext"/>, <see cref="IDomainEventsDispatcher"/>, and
/// <see cref="IUnitOfWork"/> in DI. <see cref="ITransactionalContext"/> and <see cref="IUnitOfWork"/>
/// are both registered by <c>AddUnitOfWork&lt;TContext&gt;()</c> in
/// <c>MicroKit.Persistence.EntityFrameworkCore</c>, which binds them to the same scoped
/// <c>EfUnitOfWork&lt;TContext&gt;</c> instance — so the flush targets the same
/// <c>DbContext</c> the transaction was opened on. <c>AddUnitOfWork</c> extends
/// <c>EfCoreBuilder</c>, not <c>IServiceCollection</c>; reach it through the chain:
/// <code>
/// services.AddMicroKitPersistence(p => p
///     .AddEntityFrameworkCore()
///     .AddDbContext&lt;AppDbContext&gt;(o => o.UseNpgsql(cs)) // any EF Core provider
///     .AddUnitOfWork&lt;AppDbContext&gt;());
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class TransactionBehavior<TRequest, TResponse>(
    ITransactionalContext transactionalContext,
    IDomainEventsDispatcher domainEventsDispatcher,
    IUnitOfWork unitOfWork)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public override int Order => PipelineOrder.Transaction;

    /// <inheritdoc />
    public override Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Pass-through: queries, events, and any non-command request skip the transaction.
        if (request is not ICommand and not ICommand<TResponse>)
            return next();

        return transactionalContext.ExecuteAsync<TransactionHandlerState, TResponse>(
            static async (state, ct) =>
            {
                var response = await state.Next().ConfigureAwait(false);

                // Skip event dispatch AND the flush on a business failure — no outbox rows and no
                // partial write for failed commands.
                // Fully-qualified name avoids the MicroKit.Result namespace / Result<T> type
                // ambiguity and is consistent with how LoggingBehavior calls ResultInspector.
                if (!MicroKit.MediatR.Behaviors.Pipeline.ResultInspector<TResponse>.IsFailure(response))
                {
                    await state.Dispatcher.DispatchEventsAsync(ct).ConfigureAwait(false);

                    // Flush AFTER the dispatch: aggregates and the outbox rows the dispatch just
                    // staged are written by ONE SaveChangesAsync, inside the open transaction.
                    // Ordering is load-bearing — flushing first would drop every outbox row.
                    await state.UnitOfWork.CommitAsync(ct).ConfigureAwait(false);
                }

                return response;
            },
            new TransactionHandlerState(next, domainEventsDispatcher, unitOfWork),
            cancellationToken);
    }

    /// <summary>
    /// Value-type state carrier that threads the handler delegate, the event dispatcher, and the
    /// unit of work into the static lambda without any closure allocation.
    /// The JIT specializes <c>ExecuteAsync&lt;TransactionHandlerState, TResponse&gt;</c>
    /// on the struct type, avoiding boxing.
    /// </summary>
    private readonly struct TransactionHandlerState(
        RequestHandlerDelegate<TResponse> next,
        IDomainEventsDispatcher dispatcher,
        IUnitOfWork unitOfWork)
    {
        /// <summary>The next handler delegate in the MediatR pipeline.</summary>
        public readonly RequestHandlerDelegate<TResponse> Next = next;

        /// <summary>The domain-event dispatcher that stages events before the flush.</summary>
        public readonly IDomainEventsDispatcher Dispatcher = dispatcher;

        /// <summary>The unit of work whose <c>CommitAsync</c> flushes the change tracker.</summary>
        public readonly IUnitOfWork UnitOfWork = unitOfWork;
    }
}
