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
/// <item><description><b>First non-commit exit — business failure</b> (<c>Result.IsFailure</c>): nothing is
/// dispatched and nothing is flushed, and <see cref="IUnitOfWork.DiscardChanges"/> abandons everything the
/// handler staged. The database transaction still commits, because no exception was raised — it commits
/// empty.</description></item>
/// <item><description><b>Second non-commit exit — thrown exception</b> (from the handler, the dispatch, or
/// the flush): <see cref="IUnitOfWork.DiscardChanges"/> runs before the exception is rethrown unchanged, and
/// <see cref="ITransactionalContext"/> then rolls the database transaction back.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Every non-commit exit discards — both, not one (ADR-005).</b> A database transaction rollback undoes
/// what was <em>written</em>; it does not reset the pending change set. EF Core leaves rolled-back entities
/// in the change tracker as <c>Added</c>/<c>Modified</c>, exactly as a business failure leaves them there —
/// and <c>DbContext</c> is scoped, not per-command. Wherever a scope outlives a single command (a batch
/// loop, a scheduled job, a Blazor Server circuit, an integration test chaining commands), the next
/// <c>SaveChangesAsync</c> in that scope writes the failed command's entities: a command that explicitly
/// failed persists its data anyway, with no error and no log. Discarding only on <c>Result.IsFailure</c>
/// would be half a fix — for the persistence layer the thrown path (<c>PersistenceException</c>,
/// <c>DbUpdateConcurrencyException</c>, a handler exception) is the more common failure mode.
/// </para>
/// <para>
/// <b>What the discard does not cover.</b> <see cref="IUnitOfWork.DiscardChanges"/> abandons the pending
/// change set — nothing else. Work that bypasses the change tracker and executes immediately inside the
/// ambient transaction (<c>ExecuteUpdateAsync</c>, <c>ExecuteDeleteAsync</c>, raw SQL) is already applied
/// by the time the handler returns, so on the business-failure path it is committed along with the
/// otherwise-empty transaction: a command that failed its business rule keeps those writes. Only the
/// exception path undoes them, and it is the database rollback that does so — not the discard. A command
/// that mixes set-based operations with a modelled <c>Result</c> failure must account for this.
/// </para>
/// <para>
/// The discard runs <em>inside</em> the <see cref="ITransactionalContext.ExecuteAsync{TState,TResult}"/>
/// operation rather than around it, so it lands on the retry-attempt boundary of a provider execution
/// strategy: every attempt begins from a clean change set. It lives in a <c>catch</c>, never a
/// <c>finally</c> — a successful commit must not discard. See ADR-MEDIATR-012.
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
/// <b>Nested command dispatch is not supported.</b> A command dispatched from inside another command's
/// handler would, on inner failure, discard the outer command's staged work. EF Core already rejects this
/// upstream — <c>BeginTransactionAsync</c> throws while a transaction is open on the connection — but the
/// prohibition is now load-bearing here as well, and is stated rather than left to a runtime failure.
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
                try
                {
                    var response = await state.Next().ConfigureAwait(false);

                    // Skip event dispatch AND the flush on a business failure — no outbox rows and no
                    // partial write for failed commands.
                    // Fully-qualified name avoids the MicroKit.Result namespace / Result<T> type
                    // ambiguity and is consistent with how LoggingBehavior calls ResultInspector.
                    if (MicroKit.MediatR.Behaviors.Pipeline.ResultInspector<TResponse>.IsFailure(response))
                    {
                        // Non-commit exit 1. The transaction commits clean below (nothing threw), so
                        // nothing else will ever clear what the handler staged: the scoped DbContext
                        // would otherwise carry it into the next command's SaveChangesAsync.
                        state.UnitOfWork.DiscardChanges();
                        return response;
                    }

                    await state.Dispatcher.DispatchEventsAsync(ct).ConfigureAwait(false);

                    // Flush AFTER the dispatch: aggregates and the outbox rows the dispatch just
                    // staged are written by ONE SaveChangesAsync, inside the open transaction.
                    // Ordering is load-bearing — flushing first would drop every outbox row.
                    await state.UnitOfWork.CommitAsync(ct).ConfigureAwait(false);

                    return response;
                }
                catch
                {
                    // Non-commit exit 2. The rollback that follows undoes the database work but leaves
                    // the change tracker loaded — staged entities survive a rollback exactly as they
                    // survive a business failure.
                    try
                    {
                        state.UnitOfWork.DiscardChanges();
                    }
                    catch
                    {
                        // Never mask the in-flight exception: an exception raised by cleanup inside a
                        // catch block replaces the original one and destroys the diagnosis. Unreachable
                        // on EF Core (ChangeTracker.Clear() cannot fail), but IUnitOfWork is
                        // provider-agnostic and this behavior cannot know which implementation is bound.
                        // Do not "simplify" this away — see ADR-MEDIATR-012.
                    }

                    throw; // bare rethrow — the original exception and its stack trace are preserved
                }
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

        /// <summary>
        /// The unit of work: <c>CommitAsync</c> flushes the change tracker on success,
        /// <c>DiscardChanges</c> abandons it on every non-commit exit.
        /// </summary>
        public readonly IUnitOfWork UnitOfWork = unitOfWork;
    }
}
