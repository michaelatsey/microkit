namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Carries a failed <c>Result</c> that an operation of <see cref="IResultUnitOfWork.TryExecuteAsync{TState}"/>
/// returned out of the execution strategy, so the transaction it owns is rolled back instead of committed.
/// </summary>
/// <remarks>
/// It never escapes <see cref="EfUnitOfWork{TContext}"/>: it is thrown inside the strategy's delegate,
/// goes through the attempt's rollback, and is caught once the strategy returns. It is not transient for
/// any provider strategy, which retries provider exceptions only: a custom strategy that retries every
/// exception would run the operation again.
/// </remarks>
/// <typeparam name="TResult">The <c>Result</c> type the operation returned.</typeparam>
#pragma warning disable CA1032, CA1064 // a private signal, not an exception a caller can see or build
internal sealed class FailedResultRollback<TResult>(TResult result)
    : Exception("The operation returned a failed Result: its transaction is rolled back.")
#pragma warning restore CA1032, CA1064
{
    /// <summary>The failed result to return once the transaction is rolled back.</summary>
    internal TResult Result { get; } = result;
}
