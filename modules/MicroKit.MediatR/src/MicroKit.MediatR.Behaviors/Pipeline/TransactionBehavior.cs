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
/// <item><description>The next pipeline delegate (the command handler) executes.</description></item>
/// <item><description>On business success, <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> stages
/// domain-event side-effects (outbox rows, in-process notifications) in the EF Core change tracker.</description></item>
/// <item><description><see cref="ITransactionalContext"/> commits, calling <c>SaveChangesAsync</c> and
/// committing the underlying database transaction atomically.</description></item>
/// <item><description>On any exception, the transaction is rolled back.</description></item>
/// </list>
/// </para>
/// <para>
/// The static lambda + <c>readonly struct</c> state-carrier pattern ensures zero heap allocation
/// per dispatch: no closure display class is allocated, and the JIT specializes
/// <c>ExecuteAsync&lt;TransactionHandlerState, TResponse&gt;</c> without boxing.
/// </para>
/// <para>
/// Requires <see cref="ITransactionalContext"/> and <see cref="IDomainEventsDispatcher"/> in DI.
/// <see cref="ITransactionalContext"/> is provided by
/// <c>MicroKit.Persistence.EntityFrameworkCore</c> via <c>AddEntityFrameworkCore()</c>.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class TransactionBehavior<TRequest, TResponse>(
    ITransactionalContext transactionalContext,
    IDomainEventsDispatcher domainEventsDispatcher)
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

                // Skip event dispatch on a business failure — no outbox rows for failed commands.
                // Fully-qualified name avoids the MicroKit.Result namespace / Result<T> type
                // ambiguity and is consistent with how LoggingBehavior calls ResultInspector.
                if (!MicroKit.MediatR.Behaviors.Pipeline.ResultInspector<TResponse>.IsFailure(response))
                    await state.Dispatcher.DispatchEventsAsync(ct).ConfigureAwait(false);

                return response;
            },
            new TransactionHandlerState(next, domainEventsDispatcher),
            cancellationToken);
    }

    /// <summary>
    /// Value-type state carrier that threads the handler delegate and the event dispatcher
    /// into the static lambda without any closure allocation.
    /// The JIT specializes <c>ExecuteAsync&lt;TransactionHandlerState, TResponse&gt;</c>
    /// on the struct type, avoiding boxing.
    /// </summary>
    private readonly struct TransactionHandlerState(
        RequestHandlerDelegate<TResponse> next,
        IDomainEventsDispatcher dispatcher)
    {
        /// <summary>The next handler delegate in the MediatR pipeline.</summary>
        public readonly RequestHandlerDelegate<TResponse> Next = next;

        /// <summary>The domain-event dispatcher that stages events before commit.</summary>
        public readonly IDomainEventsDispatcher Dispatcher = dispatcher;
    }
}
