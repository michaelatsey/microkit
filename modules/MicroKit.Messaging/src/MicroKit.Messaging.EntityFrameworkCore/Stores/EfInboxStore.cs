using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of the inbox stores. Provider-agnostic: no raw SQL, no
/// PostgreSQL-only construct, so the production claim path is the one exercised under SQLite in
/// integration tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>What changed and why.</b> The previous <c>MarkProcessingAsync</c> filtered on the compound
/// key alone and returned <c>void</c> — an unconditional write, not a lease. Two processors could
/// both "acquire" the same row, both invoke the handler, and neither could detect it. The inbox
/// is the system's deduplication mechanism and it double-processed under concurrency.
/// <see cref="ClaimBatchAsync"/> replaces it with a real reservation.
/// </para>
/// <para>
/// <b>Why a claim token rather than a locking clause.</b> This assembly is the provider-neutral
/// EF Core package; a PostgreSQL <c>FOR UPDATE SKIP LOCKED</c> here would leak a provider
/// dependency into a generic package and leave the production claim path untestable under
/// SQLite. The token achieves the same guarantee portably — and closes a second bug regardless
/// of performance: terminal writes filtered on the compound key alone, so a processor whose
/// lease had expired could overwrite the one that legitimately took its rows over. Every write
/// now carries <c>AND ClaimToken == token</c>.
/// </para>
/// <para>
/// <b>Two lifetimes, on purpose.</b> Resolved from a loop's scope, this type serves
/// <see cref="IInboxProcessorStore"/> for the claim and the settlement of failures. Resolved from a
/// delivery attempt's scope, it serves <see cref="IInboxGuardStore"/>: it begins the attempt's
/// transaction on the handler's own <c>DbContext</c>, writes the row there and commits it with the
/// handler's work. One scoped registration gives both: the guard necessarily shares its
/// <typeparamref name="TContext"/> with the handler resolved from the same scope.
/// </para>
/// <para>
/// <b>Schema.</b> Requires the surrogate primary key <c>RowId</c>, the unique index on
/// <c>(MessageId, ConsumerType)</c>, and a nullable <c>ClaimToken</c> mapped as a concurrency
/// token. See <see cref="InboxMessageConfiguration"/>; none of the three is optional decoration.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfInboxStore<TContext>(
    TContext context,
    TimeProvider timeProvider,
    EfInboxAttempt<TContext>? attempt = null)
    : IInboxGuardStore, IInboxProcessorStore, IInboxAdminStore, IInboxRetentionStore, IDisposable, IAsyncDisposable
    where TContext : DbContext
{
    // Logical key -> surrogate key, built from the rows this batch claimed. Scoped lifetime, so
    // it lives exactly as long as the claim it describes.
    private readonly Dictionary<InboxMessageKey, Guid> _rowIdsByKey = [];

    // ---------- IInboxGuardStore ----------

    // The attempt's transaction, begun by BeginAsync on this scope's context.
    private IDbContextTransaction? _transaction;
    private bool _committed;

    // Inside ExecuteAsync, FindAsync is the strategy's commit verification: a transient fault must
    // reach the strategy raw, so it retries the read (#188, review of 556bbc0).
    private bool _inStrategy;

    // The row this attempt staged, so CommitAsync can write it even if the handler cleared the
    // tracker: a new delivery's enlisted row, or a retry's claimed row with the claim's token.
    private InboxMessage? _enlisted;
    private (InboxMessage Row, Guid Token)? _marked;

    /// <inheritdoc/>
    public async ValueTask<InboxRowState?> FindAsync(InboxMessageKey key, CancellationToken ct = default)
    {
        var id = key.MessageId;
        var consumer = key.ConsumerType;

        var opened = await OpenOrUnreachableAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await context.Set<InboxMessage>()
                .AsNoTracking()
                .Where(m => m.MessageId == id && m.ConsumerType == consumer)
                .Select(m => new { m.Status, m.DeadLettered, m.RowId })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            return state is null ? null : new InboxRowState(state.Status, state.DeadLettered, state.RowId);
        }
        catch (Exception ex) when (IsTransientFault(ex))
        {
            throw Unreachable(ex);
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The strategy is the context's own (<c>EnableRetryOnFailure</c> or a custom one), created
    /// from this scope's context, which never opens a transaction itself: each attempt opens its own
    /// scope, context and transaction. EF refuses to run a retrying strategy over a context that
    /// already has a transaction, and accepts a transaction begun inside the strategy's operation —
    /// the shape the #188 spike proved on PostgreSQL.
    /// </remarks>
    public async ValueTask<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> operation,
        Func<TState, CancellationToken, ValueTask<bool>> verifySucceeded,
        TResult verifiedResult,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(verifySucceeded);

        var strategy = context.Database.CreateExecutionStrategy();
        _inStrategy = true;
        try
        {
            return await strategy.ExecuteAsync(
                (state, operation, verifySucceeded, verifiedResult),
                static async (_, s, token) => await s.operation(s.state, token).ConfigureAwait(false),
                static async (_, s, token) => await s.verifySucceeded(s.state, token).ConfigureAwait(false)
                    ? new ExecutionResult<TResult>(true, s.verifiedResult)
                    : new ExecutionResult<TResult>(false, default!),
                ct).ConfigureAwait(false);
        }
        catch (RetryLimitExceededException exhausted) when (exhausted.InnerException is { } last)
        {
            // The contract hands the engine the fault itself, not the strategy's wrapper.
            ExceptionDispatchInfo.Capture(last).Throw();
            throw;
        }
        finally
        {
            _inStrategy = false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask BeginAsync(CancellationToken ct = default)
    {
        if (_transaction is not null || context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "An inbox attempt was begun on a DbContext that already has a transaction. Each " +
                "attempt runs in a scope of its own, and a handler must not begin a transaction " +
                "on the store's DbContext.");
        }

        _transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        attempt?.Begun();
    }

    /// <inheritdoc/>
    public ValueTask EnlistProcessedAsync(InboxMessage row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        context.Set<InboxMessage>().Add(row);
        _enlisted = row;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Uses the tracked change pipeline rather than <c>ExecuteUpdateAsync</c>, because the write
    /// must be part of the attempt's unit of work and commit with the handler's own writes.
    /// </para>
    /// <para>
    /// <b><c>AsTracking()</c> is not decoration.</b> The store runs on the consumer's context, and
    /// <c>UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)</c> is an ordinary setting
    /// there. Under it an untracked row would take none of the changes below, the save would write
    /// nothing, and the row would keep its claim and replay on every lease expiry.
    /// </para>
    /// <para>
    /// <b>Ownership is enforced by the concurrency token, not by this read.</b> With
    /// <c>ClaimToken</c> mapped as a concurrency token, EF puts the original value in the
    /// <c>WHERE</c> clause, so a claim lost after this read matches zero rows at the save and raises
    /// <see cref="DbUpdateConcurrencyException"/> (<see cref="IsLeaseLost"/>). The original value is
    /// set from the token explicitly: the claim stamped it through <c>ExecuteUpdateAsync</c>, which
    /// bypasses the tracker.
    /// </para>
    /// </remarks>
    public async ValueTask<bool> MarkClaimedProcessedAsync(
        InboxMessageKey key, Guid claimToken, CancellationToken ct = default)
    {
        var id = key.MessageId;
        var consumer = key.ConsumerType;

        var row = await context.Set<InboxMessage>()
            .AsTracking()
            .FirstOrDefaultAsync(
                m => m.MessageId == id && m.ConsumerType == consumer && m.ClaimToken == claimToken,
                ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return false;
        }

        context.Entry(row).Property(m => m.ClaimToken).OriginalValue = claimToken;

        row.Status = InboxMessageStatus.Processed;
        row.ProcessedAtUtc = timeProvider.GetUtcNow();
        row.LockedUntilUtc = null;
        row.ClaimToken = null;
        row.NextRetryAtUtc = null;

        _marked = (row, claimToken);
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        var transaction = _transaction
            ?? throw new InvalidOperationException("An inbox attempt was committed before it was begun.");

        await RestageIfDiscardedAsync(ct).ConfigureAwait(false);

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _committed = true;
        attempt?.Committed();
    }

    /// <summary>
    /// Puts the staged row back on the tracker when the handler discarded it — a
    /// <c>ChangeTracker.Clear()</c> mid-run is ordinary in a batch-processing handler. Without this
    /// the save would write nothing for the row, the attempt would commit, and the row would stay
    /// claimed: reported handled while it replays on every lease expiry.
    /// </summary>
    /// <remarks>
    /// A handler that saved before clearing already wrote the row inside this transaction, so the
    /// database — read in the same transaction — decides; the tracker no longer can. A retry's row
    /// is recognised by the <c>ProcessedAtUtc</c> this attempt stamped: a row another loop
    /// committed carries its own.
    /// </remarks>
    private async ValueTask RestageIfDiscardedAsync(CancellationToken ct)
    {
        // Read inside the attempt's transaction, so the handler's own save is visible. The test is
        // whether THIS attempt wrote the row, not whether a row is there: one a concurrent delivery
        // or loop committed must still fail this attempt (the unique index, the claim token).
        if (_enlisted is { } enlisted && context.Entry(enlisted).State is EntityState.Detached)
        {
            var rowId = enlisted.RowId;
            var written = await context.Set<InboxMessage>().AsNoTracking()
                .AnyAsync(m => m.RowId == rowId, ct).ConfigureAwait(false);

            if (!written)
            {
                context.Set<InboxMessage>().Add(enlisted);
            }
        }

        if (_marked is { } marked && context.Entry(marked.Row).State is EntityState.Detached)
        {
            var rowId = marked.Row.RowId;
            var stamped = marked.Row.ProcessedAtUtc;

            // Compared in SQL, against the stamp as a parameter — never in memory against a value
            // read back. PostgreSQL keeps whole microseconds, so the stored stamp can differ from
            // the one in memory; the parameter goes through the same conversion as the write did.
            var written = await context.Set<InboxMessage>().AsNoTracking()
                .AnyAsync(
                    m => m.RowId == rowId && m.Status == InboxMessageStatus.Processed && m.ProcessedAtUtc == stamped,
                    ct)
                .ConfigureAwait(false);

            if (!written)
            {
                var entry = context.Set<InboxMessage>().Attach(marked.Row);
                entry.Property(m => m.ClaimToken).OriginalValue = marked.Token;
                entry.Property(m => m.Status).IsModified = true;
                entry.Property(m => m.ProcessedAtUtc).IsModified = true;
                entry.Property(m => m.LockedUntilUtc).IsModified = true;
                entry.Property(m => m.ClaimToken).IsModified = true;
                entry.Property(m => m.NextRetryAtUtc).IsModified = true;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsLeaseLost(Exception exception) =>
        exception is DbUpdateConcurrencyException concurrency
        && concurrency.Entries.Any(e => e.Entity is InboxMessage);

    /// <inheritdoc/>
    /// <remarks>
    /// The duplicate is recognised by finding the row after the failed insert, never by decoding a
    /// provider error code: the question is "is the row there?", which needs no provider knowledge.
    /// No transaction is open here, so the failed insert leaves nothing to unwind.
    /// </remarks>
    public async ValueTask<bool> TryInsertFailedAsync(InboxMessage row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        var opened = await OpenOrUnreachableAsync(ct).ConfigureAwait(false);
        var entry = context.Set<InboxMessage>().Add(row);
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsTransientFault(ex))
        {
            entry.State = EntityState.Detached;
            throw Unreachable(ex);
        }
        catch (DbUpdateException)
        {
            entry.State = EntityState.Detached;

            if (await FindAsync(new InboxMessageKey(row.MessageId, row.ConsumerType), ct).ConfigureAwait(false) is not null)
            {
                return false;
            }

            throw;
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Opens the connection on its own, so a database that cannot be reached is told apart from a
    /// command that fails on its data (#188): the first is a dependency outage, the second the
    /// failure it is. Returns whether this call opened it, and must therefore close it.
    /// </summary>
    private async ValueTask<bool> OpenOrUnreachableAsync(CancellationToken ct)
    {
        if (context.Database.GetDbConnection().State is ConnectionState.Open)
        {
            return false;
        }

        try
        {
            await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!_inStrategy && ex is not OperationCanceledException and not ObjectDisposedException)
        {
            throw Unreachable(ex);
        }
    }

    /// <summary>
    /// A fault the provider calls transient — a dropped connection, a timeout — outside the
    /// strategy. Never a cancellation of a cancelled token: one during a host stop must reach the
    /// engine as the stop it is, which charges nothing (D10), not as an outage.
    /// </summary>
    private bool IsTransientFault(Exception ex)
        => !_inStrategy
            && ex is not OperationCanceledException { CancellationToken.IsCancellationRequested: true }
            && (ex is DbException { IsTransient: true } || ex.InnerException is DbException { IsTransient: true });

    private static InboxDependencyUnavailableException Unreachable(Exception ex)
        => new($"The inbox store of '{typeof(TContext).FullName}' cannot be reached: {ex.Message}", ex);

    /// <inheritdoc/>
    /// <remarks>
    /// Both disposal forms, with the same logic: the container calls <see cref="Dispose"/> for a
    /// scope disposed synchronously, and throws for a service that is only
    /// <see cref="IAsyncDisposable"/>. An attempt's transaction not committed is rolled back with it.
    /// </remarks>
    public void Dispose()
    {
        var transaction = DiscardUncommitted();
        transaction?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        var transaction = DiscardUncommitted();
        if (transaction is not null)
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// An attempt that never committed — its handler threw, its save hit the inbox key — rolls back,
    /// and what it staged goes with it: the tracker is cleared, so a publication it enlisted reads as
    /// discarded with its work (4001), never as lost (4000).
    /// </summary>
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? DiscardUncommitted()
    {
        var transaction = _transaction;
        _transaction = null;
        if (transaction is not null && !_committed)
        {
            try
            {
                context.ChangeTracker.Clear();
            }
            catch (ObjectDisposedException)
            {
                // The context went first: nothing is left to clear.
            }
        }

        return transaction;
    }

    // ---------- IInboxProcessorStore ----------

    /// <inheritdoc/>
    public async ValueTask<InboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var lockExpiry = now.Add(leaseDuration);
        var token = Guid.NewGuid();

        // 1. Candidates, selected by their single-column surrogate key.
        //
        //    The logical key is compound, and filtering an UPDATE with
        //    `ids.Contains(MessageId) && consumers.Contains(ConsumerType)` selects the CROSS
        //    PRODUCT of the two lists, not the candidate pairs. That claims rows nobody chose
        //    and breaks the "at most batchSize" contract outright: 100 candidates spanning 5
        //    consumers can match up to 500 rows. RowId collapses it to one exact, bounded list.
        //
        //    A separate query rather than OrderBy/Take inside ExecuteUpdate: whether
        //    row-limiting operators translate there varies by EF Core version and provider, and
        //    the claim path is the wrong place to rely on an unverified capability.
        var candidateRowIds = await context.Set<InboxMessage>()
            .AsNoTracking()
            .Where(Processable(now))
            .OrderBy(m => m.ReceivedAtUtc)
            .Take(batchSize)
            .Select(m => m.RowId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidateRowIds.Count == 0)
        {
            return InboxClaim.Empty;
        }

        // Sorted so concurrent processors lock overlapping rows in the same order. Without it,
        // two processors whose candidate sets intersect can lock in opposite orders and
        // deadlock. Low probability — same index, same plan — but free to eliminate.
        // Guid is IComparable<Guid>, so no ordering support has to be added for this.
        candidateRowIds.Sort();

        // 2. Stamp. The eligibility predicate is re-evaluated inside the UPDATE, so a row
        //    another processor claimed between step 1 and step 2 simply does not match. This is
        //    the atomic point of the claim: whoever's UPDATE lands first owns the row. Steps 1
        //    and 3 are not part of it, which is why this is an optimistic strategy and why it is
        //    validated by a real concurrency test rather than by reading.
        var claimedCount = await context.Set<InboxMessage>()
            .Where(m => candidateRowIds.Contains(m.RowId))
            .Where(Processable(now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, InboxMessageStatus.Processing)
                .SetProperty(m => m.LockedUntilUtc, lockExpiry)
                .SetProperty(m => m.ClaimToken, token),
                ct)
            .ConfigureAwait(false);

        if (claimedCount == 0)
        {
            return InboxClaim.Empty;
        }

        // 3. Read back by token — the only way to know which candidates were actually won when
        //    some were lost to a competing processor. Bounded by step 1, so it can never exceed
        //    batchSize.
        var messages = await context.Set<InboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimToken == token)
            .OrderBy(m => m.ReceivedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _rowIdsByKey.Clear();
        foreach (var claimed in messages)
        {
            _rowIdsByKey[new InboxMessageKey(claimed.MessageId, claimed.ConsumerType)] = claimed.RowId;
        }

        return new InboxClaim(token, messages);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Wrapped in an explicit transaction. Each <c>ExecuteUpdateAsync</c> otherwise runs in its
    /// own implicit transaction, so a database fault partway through would leave some rows
    /// settled and others still leased.
    /// </para>
    /// <para>
    /// <b>Run as one operation of the context's execution strategy</b>
    /// (<see cref="SettlementTransaction"/>). A retrying strategy (<c>EnableRetryOnFailure</c>)
    /// refuses a transaction begun outside it, which would leave every failed retry unsettled —
    /// re-claimed on lease expiry with no attempt ever counted, so it never dead-letters. A commit
    /// whose outcome is unknown is verified by the claim token, which the commit clears.
    /// </para>
    /// </remarks>
    public ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct = default)
        => SettlementTransaction.RunAsync(
            context,
            (store: this, claimToken, outcomes),
            static (s, token) => s.store.ApplyOutcomesCoreAsync(s.claimToken, s.outcomes, token),
            static (s, token) => s.store.IsSettledAsync(s.claimToken, s.outcomes, token),
            ct);

    /// <summary>Whether no row of <paramref name="outcomes"/> still carries the claim token.</summary>
    private async ValueTask<bool> IsSettledAsync(
        Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct)
    {
        var stillClaimed = await context.Set<InboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimToken == claimToken)
            .Select(m => new InboxMessageKey(m.MessageId, m.ConsumerType))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return !outcomes.Any(o => stillClaimed.Contains(o.Key));
    }

    private async ValueTask<int> ApplyOutcomesCoreAsync(
        Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var written = 0;

        {
            // Released and Processed are set-based: one statement each, whatever the batch size.
            written += await SettleGroupAsync(
                claimToken, outcomes, InboxOutcomeKind.Released,
                s => s.SetProperty(m => m.Status, InboxMessageStatus.Failed)
                      .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                      .SetProperty(m => m.ClaimToken, (Guid?)null),
                ct).ConfigureAwait(false);

            written += await SettleGroupAsync(
                claimToken, outcomes, InboxOutcomeKind.Processed,
                s => s.SetProperty(m => m.Status, InboxMessageStatus.Processed)
                      .SetProperty(m => m.ProcessedAtUtc, now)
                      .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                      .SetProperty(m => m.ClaimToken, (Guid?)null),
                ct).ConfigureAwait(false);

            // Retries and dead-letters carry per-row values (retry count, next attempt, error
            // text), so they cannot collapse into one statement without a server-side join over
            // a values list — provider-specific, and it would forfeit SQLite testing. The cost
            // is bounded by the number of FAILED rows, not by batch size: in normal operation
            // this loop does not execute at all.
            foreach (var outcome in outcomes)
            {
                if (!_rowIdsByKey.TryGetValue(outcome.Key, out var rowId))
                {
                    continue;
                }

                switch (outcome.Kind)
                {
                    case InboxOutcomeKind.Retry:
                        written += await context.Set<InboxMessage>()
                            .Where(m => m.RowId == rowId && m.ClaimToken == claimToken)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(m => m.Status, InboxMessageStatus.Failed)
                                .SetProperty(m => m.RetryCount, outcome.RetryCount)
                                .SetProperty(m => m.NextRetryAtUtc, outcome.NextRetryAtUtc)
                                .SetProperty(m => m.ErrorMessage, outcome.ErrorMessage)
                                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                                .SetProperty(m => m.ClaimToken, (Guid?)null),
                                ct)
                            .ConfigureAwait(false);
                        break;

                    case InboxOutcomeKind.DeadLetter:
                        written += await context.Set<InboxMessage>()
                            .Where(m => m.RowId == rowId && m.ClaimToken == claimToken)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(m => m.Status, InboxMessageStatus.Failed)
                                .SetProperty(m => m.DeadLettered, true)
                                .SetProperty(m => m.RetryCount, outcome.RetryCount)
                                .SetProperty(m => m.ProcessedAtUtc, now)
                                .SetProperty(m => m.ErrorMessage, outcome.ErrorMessage)
                                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                                .SetProperty(m => m.ClaimToken, (Guid?)null),
                                ct)
                            .ConfigureAwait(false);
                        break;

                    default:
                        break;
                }
            }

            return written;
        }
    }

    // ---------- IInboxAdminStore ----------

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<InboxMessage>> GetDeadLetteredAsync(
        int batchSize, string? tenantId = null, CancellationToken ct = default)
    {
        // tenantId null means "every tenant". The previous signature took a non-nullable string,
        // so single-tenant deployments — where every row has a null TenantId — never matched.
        return await context.Set<InboxMessage>()
            .AsNoTracking()
            .Where(m => m.DeadLettered)
            .Where(m => tenantId == null || m.TenantId == tenantId)
            .OrderBy(m => m.ReceivedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RequeueAsync(InboxMessageKey key, CancellationToken ct = default)
    {
        var id = key.MessageId;
        var consumer = key.ConsumerType;

        var rows = await context.Set<InboxMessage>()
            .Where(m => m.MessageId == id && m.ConsumerType == consumer && m.DeadLettered)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, InboxMessageStatus.Failed)
                .SetProperty(m => m.DeadLettered, false)
                .SetProperty(m => m.RetryCount, 0)
                .SetProperty(m => m.NextRetryAtUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ErrorMessage, (string?)null)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ClaimToken, (Guid?)null),
                ct)
            .ConfigureAwait(false);

        // Affected-row count rather than an unconditional success: zero rows means the message
        // was not dead-lettered, which the operator must be able to see.
        return rows == 1;
    }

    // ---------- IInboxRetentionStore ----------

    /// <inheritdoc/>
    public async ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string? tenantId = null, CancellationToken ct = default)
    {
        return await context.Set<InboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Processed && m.ProcessedAtUtc < olderThan)
            .Where(m => tenantId == null || m.TenantId == tenantId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    // ---------- helpers ----------

    /// <summary>
    /// A row is retryable when its last attempt failed (or it was received by an earlier version and
    /// never handled) — or it is claimed with an expired lease, which is how a crashed loop's work is
    /// recovered — is not dead-lettered, and has reached its back-off deadline.
    /// </summary>
    private static Expression<Func<InboxMessage, bool>> Processable(DateTimeOffset now) =>
        m => !m.DeadLettered
             && (m.Status == InboxMessageStatus.Failed
                 || m.Status == InboxMessageStatus.Received
                 || (m.Status == InboxMessageStatus.Processing && m.LockedUntilUtc <= now))
             && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now);

    private async ValueTask<int> SettleGroupAsync(
        Guid claimToken,
        IReadOnlyList<InboxOutcome> outcomes,
        InboxOutcomeKind kind,
        Action<UpdateSettersBuilder<InboxMessage>> setters,
        CancellationToken ct)
    {
        var rowIds = ResolveRowIds(outcomes, kind);

        if (rowIds.Count == 0)
        {
            return 0;
        }

        return await context.Set<InboxMessage>()
            .Where(m => rowIds.Contains(m.RowId) && m.ClaimToken == claimToken)
            .ExecuteUpdateAsync(setters, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Maps the logical keys of one outcome kind back to surrogate keys, using the rows this
    /// batch already loaded.
    /// </summary>
    /// <remarks>
    /// The claim returned full entities, so no round trip is needed: the mapping is built once
    /// per batch from what is already in memory. Settling by <c>RowId</c> avoids the compound-key
    /// cross product that would otherwise let one consumer's outcome touch a sibling's row.
    /// </remarks>
    private List<Guid> ResolveRowIds(IReadOnlyList<InboxOutcome> outcomes, InboxOutcomeKind kind)
    {
        var rowIds = new List<Guid>();

        foreach (var outcome in outcomes)
        {
            if (outcome.Kind == kind && _rowIdsByKey.TryGetValue(outcome.Key, out var rowId))
            {
                rowIds.Add(rowId);
            }
        }

        return rowIds;
    }
}
