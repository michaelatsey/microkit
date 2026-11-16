using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using Microsoft.EntityFrameworkCore.Storage;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// Runs a store's batch settlement in a transaction of its own, as one operation of the context's
/// execution strategy. Shared by the outbox and the inbox stores.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why inside the strategy.</b> A retrying strategy (<c>EnableRetryOnFailure</c>) refuses a
/// transaction begun outside it: a settlement that did so would throw on every batch, and every
/// row would be claimed again on each lease expiry, never settled.
/// </para>
/// <para>
/// <b>A commit whose outcome is unknown is verified, not replayed blind.</b> Every settlement write
/// is conditioned on the claim token and clears it, so a replay after a commit that did land writes
/// zero rows, and would report a settled batch as lost leases. The verification asks whether any
/// row of the outcomes still carries the token: none means the commit landed, and the count is the
/// one that attempt wrote.
/// </para>
/// </remarks>
internal static class SettlementTransaction
{
    /// <summary>Runs <paramref name="settle"/> in its own transaction, or in the ambient one.</summary>
    /// <param name="context">The store's context; the strategy and the transaction are its own.</param>
    /// <param name="state">What both callbacks read.</param>
    /// <param name="settle">Writes the settlement and returns the affected-row count.</param>
    /// <param name="settled">Whether the settlement has committed: no row still carries the token.</param>
    /// <param name="ct">The settlement's own timeout.</param>
    /// <typeparam name="TState">The callbacks' state.</typeparam>
    /// <returns>The affected-row count.</returns>
    /// <remarks>Out of retries, the last fault propagates itself, not the strategy's wrapper.</remarks>
    public static async ValueTask<int> RunAsync<TState>(
        DbContext context,
        TState state,
        Func<TState, CancellationToken, ValueTask<int>> settle,
        Func<TState, CancellationToken, ValueTask<bool>> settled,
        CancellationToken ct)
    {
        // An ambient transaction is the host's: join it, and leave its retries to its owner.
        if (context.Database.CurrentTransaction is not null)
        {
            return await settle(state, ct).ConfigureAwait(false);
        }

        // The count the attempt that reached the commit wrote; a verified commit returns it.
        var written = new StrongBox<int>();
        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(
                (context, state, settle, settled, written),
                static async (_, run, token) =>
                {
                    var transaction = await run.context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                    await using (transaction.ConfigureAwait(false))
                    {
                        run.written.Value = await run.settle(run.state, token).ConfigureAwait(false);
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                        return run.written.Value;
                    }
                },
                static async (_, run, token) => await run.settled(run.state, token).ConfigureAwait(false)
                    ? new ExecutionResult<int>(true, run.written.Value)
                    : new ExecutionResult<int>(false, 0),
                ct).ConfigureAwait(false);
        }
        catch (RetryLimitExceededException exhausted) when (exhausted.InnerException is { } last)
        {
            ExceptionDispatchInfo.Capture(last).Throw();
            throw;
        }
    }
}
