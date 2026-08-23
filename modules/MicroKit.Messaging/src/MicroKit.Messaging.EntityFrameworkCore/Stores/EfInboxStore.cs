using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;

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
/// <b>Two lifetimes, on purpose.</b> Resolved from the batch scope, this type serves
/// <see cref="IInboxProcessorStore"/> for claim and deferred settlement. Resolved from the
/// per-message execution scope, it serves <see cref="IInboxSettlementStore"/> and stages the
/// processed mark on the handler's own <c>DbContext</c>, so it commits in the handler's
/// transaction. One scoped registration gives both: scope identity does the separation, and the
/// settlement instance necessarily shares its <typeparamref name="TContext"/> with the handler
/// resolved from the same scope.
/// </para>
/// <para>
/// <b>Schema.</b> Requires the surrogate primary key <c>RowId</c>, the unique index on
/// <c>(MessageId, ConsumerType)</c>, and a nullable <c>ClaimToken</c> mapped as a concurrency
/// token. See <see cref="InboxMessageConfiguration"/>; none of the three is optional decoration.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfInboxStore<TContext>(TContext context, TimeProvider timeProvider)
    : IInboxWriter, IInboxProcessorStore, IInboxSettlementStore, IInboxAdminStore, IInboxRetentionStore
    where TContext : DbContext
{
    // Logical key -> surrogate key, built from the rows this batch claimed. Scoped lifetime, so
    // it lives exactly as long as the claim it describes.
    private readonly Dictionary<InboxMessageKey, Guid> _rowIdsByKey = [];

    // ---------- IInboxWriter ----------

    /// <inheritdoc/>
    public async ValueTask<bool> ExistsAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default)
    {
        return await context.Set<InboxMessage>()
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId && m.ConsumerType == consumerType, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>How the duplicate is recognised.</b> Not by decoding a provider error code. The insert
    /// is attempted, and if it fails the row is queried: if it is there now, the gate held and
    /// this is a redelivery. That asks the question the decision actually depends on — "is the
    /// message recorded?" — rather than the syntactic one a detector answers, "was that error a
    /// unique violation?". It costs nothing in provider knowledge and keeps working on providers
    /// that do not exist yet. It is a check <i>after</i> the failed insert, never a guard before
    /// it, so the unique index remains the sole authority and there is no
    /// time-of-check-to-time-of-use window.
    /// </para>
    /// <para>
    /// The alternatives were weighed and rejected. EF Core 10 still wraps every database fault in
    /// <see cref="DbUpdateException"/> and exposes no typed constraint exception. The
    /// <c>EntityFramework.Exceptions</c> package supplies one, but it activates through
    /// <c>UseExceptionProcessor</c> on the <b>consumer's</b> <c>DbContext</c>, which a library
    /// cannot configure — a consumer who forgets it would silently reproduce the defect. A
    /// hand-written per-provider detector reads <c>SqlState</c>, which does not survive a
    /// <c>DEFERRABLE INITIALLY DEFERRED</c> unique constraint on PostgreSQL, where the inner
    /// exception is not a provider exception at all.
    /// </para>
    /// <para>
    /// <b>Why the savepoint.</b> On PostgreSQL a constraint violation aborts the entire
    /// transaction; every later statement on that connection fails until a rollback. Without an
    /// ambient transaction the implicit one EF opens absorbs this. Under an ambient transaction —
    /// a broker adapter ingesting several messages atomically, or a persistence behaviour that
    /// opened one — nothing would work afterwards, <b>including the verification query in this
    /// very catch block</b>. Note a per-provider detector would not have helped either: it
    /// classifies the exception correctly and then returns into a transaction that is already
    /// dead.
    /// </para>
    /// <para>
    /// EF Core creates its own automatic savepoint around <c>SaveChanges</c> inside a
    /// manually-started transaction, so in the default configuration this one is belt and braces.
    /// It becomes load-bearing when a consumer sets
    /// <c>Database.AutoSavepointsEnabled = false</c>, and making the guarantee independent of a
    /// consumer setting is the point.
    /// </para>
    /// <para>
    /// <b>Provider limits.</b> The savepoint name is kept to 24 characters because SQL Server
    /// caps savepoint identifiers at 32 and rejects longer ones; where a name is truncated rather
    /// than rejected, two savepoints can end up sharing a name and a rollback then unwinds to the
    /// wrong one without complaint. SQL Server also rejects savepoints inside a distributed
    /// transaction — ingest outside one.
    /// </para>
    /// <para>
    /// <b>Known gap.</b> If a retention job deletes the row between the failed insert and the
    /// verification, the duplicate is rethrown as a fault. Vanishingly rare, and it fails in the
    /// safe direction: a visible error rather than a silent loss.
    /// </para>
    /// </remarks>
    public async ValueTask<InboxWriteResult> AddAsync(
        InboxMessage message, CancellationToken ct = default)
    {
        var ambient = context.Database.CurrentTransaction;

        // 24 characters. SQL Server limits savepoint identifiers to 32 and rejects longer ones
        // outright; a full "N"-formatted Guid with any prefix exceeds that. PostgreSQL tolerates
        // 63, so a Testcontainers suite would never catch it — the exact provider blind spot this
        // package claims to avoid. 17 hex characters is ample entropy for a name that only has to
        // be unique inside one transaction.
        var savepoint = ambient is null
            ? null
            : $"mk_ibx_{Guid.NewGuid():N}"[..24];

        if (savepoint is not null)
        {
            if (!ambient!.SupportsSavepoints)
            {
                // Proceeding would leave the caller's transaction unusable after a duplicate,
                // and a duplicate is the nominal path here. Refusing loudly beats corrupting a
                // transaction the caller believes is healthy.
                throw new InboxConfigurationException(
                    "Inbox ingestion is running inside an ambient transaction on a provider that " +
                    "does not support savepoints. A duplicate would abort that transaction with " +
                    "no way to recover. Ingest outside the transaction, or use a provider with " +
                    "savepoint support.");
            }

            await ambient.CreateSavepointAsync(savepoint, ct).ConfigureAwait(false);
        }

        var entry = context.Set<InboxMessage>().Add(message);

        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);

            if (savepoint is not null)
            {
                // Released on success so a long ingestion loop does not accumulate one
                // subtransaction per message.
                await ambient!.ReleaseSavepointAsync(savepoint, ct).ConfigureAwait(false);
            }

            return InboxWriteResult.Added;
        }
        catch (DbUpdateException)
        {
            // Detach first: the rejected entity would otherwise stay tracked as added, and the
            // next SaveChangesAsync on this context would retry the same failing insert — so one
            // duplicate would break every later write on that context rather than just its own.
            entry.State = EntityState.Detached;

            if (savepoint is not null)
            {
                // Must precede any other statement: on PostgreSQL the connection refuses
                // everything until the aborted transaction is unwound.
                await ambient!.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);
            }

            if (await ExistsAsync(message.MessageId, message.ConsumerType, ct).ConfigureAwait(false))
            {
                return InboxWriteResult.AlreadyPresent;
            }

            // The row is not there, so whatever failed was not the dedup gate. Absorbing this
            // would trade a spurious dead-letter for silent data loss, which is worse.
            throw;
        }
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
    /// Wrapped in an explicit transaction. Each <c>ExecuteUpdateAsync</c> otherwise runs in its
    /// own implicit transaction, so a database fault partway through would leave some rows
    /// settled and others still leased.
    /// </remarks>
    public async ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var written = 0;

        // Respect an ambient transaction if the host started one; otherwise own the scope.
        var ownsTransaction = context.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        try
        {
            // Released and Processed are set-based: one statement each, whatever the batch size.
            written += await SettleGroupAsync(
                claimToken, outcomes, InboxOutcomeKind.Released,
                s => s.SetProperty(m => m.Status, InboxMessageStatus.Received)
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
                                .SetProperty(m => m.Status, InboxMessageStatus.Received)
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

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            return written;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    // ---------- IInboxSettlementStore ----------

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Uses the tracked change pipeline rather than <c>ExecuteUpdateAsync</c>, precisely because
    /// it must <b>not</b> execute immediately: the write has to be staged so the handler's
    /// <c>SaveChangesAsync</c> commits it in the same transaction as the handler's own side
    /// effects. <c>ExecuteUpdate</c> bypasses the change tracker and would commit on its own,
    /// reopening the very gap this method exists to close.
    /// </para>
    /// <para>
    /// <b>Ownership is enforced by the concurrency token, not by this read.</b> Filtering the
    /// query on <c>ClaimToken</c> proves ownership at read time only; the <c>UPDATE</c> emitted
    /// later by <c>SaveChanges</c> would otherwise carry the primary key alone, so a lease that
    /// expired in between would be silently overwritten. With <c>ClaimToken</c> mapped as a
    /// concurrency token, EF puts the original value in the <c>WHERE</c> clause, the update
    /// matches zero rows, and <see cref="DbUpdateConcurrencyException"/> rolls the handler
    /// transaction back with the mark. See <see cref="InboxMessageConfiguration"/>.
    /// </para>
    /// </remarks>
    public async ValueTask<bool> StageProcessedAsync(
        InboxMessageKey key, Guid claimToken, CancellationToken ct = default)
    {
        var id = key.MessageId;
        var consumer = key.ConsumerType;

        // AsTracking() is not decoration and not a default being restated. This is the ONE query
        // in this file that requires tracking — the other four all say AsNoTracking() — and
        // without it the whole guarantee is one consumer setting away from silently not existing:
        //
        //   optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
        //
        // is an ordinary, widely recommended setting on a read-heavy application's DbContext, and
        // this store runs on the consumer's context. Under it the row comes back untracked, the
        // mutations below reach nothing the change tracker will save, the handler's SaveChanges
        // writes zero rows, and — because ChangeTracker.Entries<T>() excludes untracked entities —
        // IsMarkUncommitted reports the mark as committed. The row keeps its ClaimToken, is
        // re-claimed on every lease expiry, and replays forever while the batch reports it
        // Processed. Verified empirically before this line existed, not inferred.
        //
        // Same reasoning that rejected EntityFramework.Exceptions for the dedup gate: a
        // correctness guarantee must not depend on a consumer configuring something the library
        // cannot control.
        var row = await context.Set<InboxMessage>()
            .AsTracking()
            .FirstOrDefaultAsync(
                m => m.MessageId == id && m.ConsumerType == consumer && m.ClaimToken == claimToken,
                ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            // No row under this token: the lease expired and another processor owns it.
            // Reported rather than swallowed — the caller must not invoke the handler.
            return false;
        }

        // The query above proved the database row carries this token, so state that as the
        // version being updated from. Without it the concurrency check asserts whatever the
        // change tracker happened to hold: ClaimBatchAsync stamps the token through
        // ExecuteUpdateAsync, which bypasses the tracker entirely, so a context that had already
        // materialised this row still believes ClaimToken is null. EF would then emit
        // `WHERE ClaimToken IS NULL`, match zero rows, and report a lost lease on a row this
        // processor demonstrably owns.
        //
        // In production the claim and the settlement run on different scopes and therefore
        // different contexts, so the stale-tracker case does not arise — but a guarantee that
        // holds only because two collaborators happen not to share a context is not a guarantee.
        context.Entry(row).Property(m => m.ClaimToken).OriginalValue = claimToken;

        row.Status = InboxMessageStatus.Processed;
        row.ProcessedAtUtc = timeProvider.GetUtcNow();
        row.LockedUntilUtc = null;
        row.ClaimToken = null;

        // Deliberately no SaveChangesAsync: the handler's unit of work owns the boundary.
        return true;
    }

    /// <inheritdoc/>
    public bool IsMarkUncommitted(InboxMessageKey key)
    {
        var entry = context.ChangeTracker
            .Entries<InboxMessage>()
            .FirstOrDefault(e =>
                e.Entity.MessageId == key.MessageId && e.Entity.ConsumerType == key.ConsumerType);

        // Three cases, and the third is the one that matters.
        //
        //   Unchanged  -> EF reset the entry after a successful save. The mark is durable.
        //   Modified   -> SaveChanges was never called. The mark is in memory only.
        //   absent or Detached -> UNKNOWN, and unknown must be reported as uncommitted.
        //
        // The previous version returned false for the third case, reasoning that no entry means
        // nothing is left pending. That is literally true and operationally wrong: the question
        // this method answers is "was the mark persisted?", not "is anything still queued". A
        // handler that calls ChangeTracker.Clear() mid-run, or detaches the entity, discards the
        // staged mark — nothing is pending precisely because the write was thrown away.
        //
        // Reporting that as committed makes the processor return Settled, buffer no outcome, and
        // leave the row claimed: it replays on every pass, never increments RetryCount, and never
        // dead-letters, while every batch reports it Processed. Reporting it as uncommitted costs
        // at worst one redundant deferred write plus a warning. A redundant write is recoverable;
        // a lost mark is a silent infinite replay, so the asymmetry decides the default.
        return entry is not { State: EntityState.Unchanged };
    }

    /// <inheritdoc/>
    public bool IsLeaseLost(Exception exception) =>
        exception is DbUpdateConcurrencyException concurrency
        && concurrency.Entries.Any(e => e.Entity is InboxMessage);

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
                .SetProperty(m => m.Status, InboxMessageStatus.Received)
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
    /// A row is processable when it is received — or processing with an expired lease, which is
    /// how a crashed processor's work is recovered — is not dead-lettered, and has reached its
    /// back-off deadline.
    /// </summary>
    private static Expression<Func<InboxMessage, bool>> Processable(DateTimeOffset now) =>
        m => !m.DeadLettered
             && (m.Status == InboxMessageStatus.Received
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
