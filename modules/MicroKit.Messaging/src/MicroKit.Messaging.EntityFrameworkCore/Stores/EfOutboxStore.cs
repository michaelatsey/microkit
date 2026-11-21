using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of the outbox stores. Provider-agnostic by design: no raw SQL,
/// no PostgreSQL-only construct, so the same code path runs under SQLite in integration
/// tests as in production.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a claim token rather than <c>FOR UPDATE SKIP LOCKED</c>.</b> This assembly is
/// the provider-neutral EF Core package; a PostgreSQL locking clause here would leak a
/// provider dependency into a generic package and leave the production claim path
/// untestable under SQLite. The token achieves the same guarantee portably — and closes
/// a bug the previous design had regardless of performance: terminal writes filtered on
/// message id alone, so a processor whose lease had expired could overwrite the
/// processor that legitimately took its messages over. Every write now carries
/// <c>AND ClaimToken == token</c>.
/// </para>
/// <para>
/// <b>Correctness of the claim.</b> The eligibility predicate is replayed inside the
/// stamping <c>UPDATE</c>. Under <c>READ COMMITTED</c> — the PostgreSQL default — a blocked
/// <c>UPDATE</c> re-evaluates its <c>WHERE</c> against the committed row version once the lock is
/// released, so the loser of a race simply does not match the row. That is the mechanism this
/// design rests on; it is proven by the concurrent-processor tests rather than assumed.
/// </para>
/// <para>
/// <b>Round trips.</b> Two in the uncontended case (select candidates, stamp them), three
/// when another processor won some rows and the winners must be re-read, plus one to
/// settle. Against <c>2N+1</c> before. A single-round-trip claim via
/// <c>UPDATE … RETURNING</c> remains possible in a future PostgreSQL-specific package;
/// it is not worth a provider dependency today.
/// </para>
/// <para>
/// <b>Schema change.</b> Requires a nullable <c>ClaimToken</c> (uuid) column on the
/// outbox table, indexed.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfOutboxStore<TContext>(
    TContext context,
    TimeProvider timeProvider,
    ILogger<EfOutboxStore<TContext>>? logger = null,
    EfInboxAttempt<TContext>? attempt = null)
    : IOutboxWriter, IOutboxProcessorStore, IOutboxAdminStore, IOutboxRetentionStore, IDisposable, IAsyncDisposable
    where TContext : DbContext
{
    private readonly ILogger _logger = logger ?? NullLogger<EfOutboxStore<TContext>>.Instance;
    private List<OutboxMessage>? _enlisted;

    // ---------- IOutboxWriter ----------
    // Stages the row; the caller's UoW (SaveChanges) commits it atomically with domain
    // changes. Do NOT call SaveChangesAsync here.

    /// <inheritdoc/>
    public ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        context.Set<OutboxMessage>().Add(message);
        (_enlisted ??= []).Add(message);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask AddBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        context.Set<OutboxMessage>().AddRange(messages);
        (_enlisted ??= []).AddRange(messages);
        return ValueTask.CompletedTask;
    }

    // ---------- end of scope: an enlisted row is never lost silently ----------

    /// <summary>
    /// Reports every row this scope enlisted and never saved. Runs when the scope is disposed:
    /// the store depends on the context, so the container disposes it first, while the context is
    /// still usable.
    /// </summary>
    /// <remarks>
    /// A row still <c>Added</c> is a publication whose unit of work was never saved — lost with the
    /// scope, so an <c>Error</c>. A row <c>Detached</c> was discarded on purpose with the change
    /// tracker — a rolled-back attempt, <c>DiscardChanges</c> — together with the business data it
    /// announced, so a <c>Debug</c>; so is a row still <c>Added</c> when this scope's guarded inbox
    /// attempt never committed (<see cref="EfInboxAttempt{TContext}"/>): it rolls back with that work. A row saved and then rolled back by its transaction is neither:
    /// the business data rolled back with it.
    /// </remarks>
    public void Dispose()
    {
        if (_enlisted is null)
        {
            return;
        }

        var store = EntityFrameworkOutbox.KeyOf<TContext>().Value;
        var enlisted = _enlisted;
        _enlisted = null;

        // This scope's guarded attempt never committed: it rolls back, and an Added row goes with the
        // work it announced — not lost. Any other open transaction (a host's own, through
        // UseTransaction) proves nothing: an Added row there was never saved, and is lost.
        var rollingBack = attempt is { Uncommitted: true };

        foreach (var row in enlisted)
        {
            EntityState state;
            try
            {
                state = context.Entry(row).State;
            }
            catch (ObjectDisposedException)
            {
                // The context went first (a host disposing it by hand): nothing can be read.
                return;
            }

            if (state is EntityState.Added && !rollingBack)
            {
                EfOutboxLogs.UnsavedEnlistment(_logger, row.Id.Value, row.ContractName ?? row.EventType, store);
            }
            else if (state is EntityState.Detached or EntityState.Added)
            {
                EfOutboxLogs.DiscardedEnlistment(_logger, row.Id.Value, row.ContractName ?? row.EventType, store);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same report as <see cref="Dispose"/>: the container calls this for a scope disposed
    /// asynchronously, that one for a scope disposed synchronously, so the report never depends on
    /// how the scope ends.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- IOutboxProcessorStore ----------

    /// <inheritdoc/>
    public async ValueTask<OutboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan lockDuration, OutboxEndpoints excluded, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var lockExpiry = now.Add(lockDuration);
        var token = Guid.NewGuid();

        // 1. Candidates. Deliberately a separate query rather than OrderBy/Take inside
        //    ExecuteUpdate: whether row-limiting operators are translatable inside an
        //    ExecuteUpdate source query varies by EF Core version and provider, and the
        //    claim path is the wrong place to depend on an unverified capability.
        //
        //    Full rows, not ids alone. That is what lets step 3 be skipped outright when
        //    nothing was contended — the normal case. The cost is paid only under contention,
        //    where the payloads of the rows we lose are transferred once for nothing.
        //    Ordered on CreatedAtUtc, NOT OccurredOnUtc: queue position must not depend on a
        //    business timestamp the caller supplies, or a backdated event jumps every row ahead of
        //    it. This clause and the contended read-back below must always agree — two different
        //    sorts would make the claim's contents depend on whether it was contended.
        //    The excluded endpoints' rows are left out here only: the stamp below matches the
        //    candidates by id, and the read-back by token.
        var candidates = await Excluding(Dispatchable(now), excluded)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return OutboxClaim.Empty;
        }

        var candidateIds = candidates.ConvertAll(m => m.Id);

        // Sorted so concurrent processors lock overlapping rows in the same order. Without
        // it, two processors whose candidate sets intersect can lock in opposite orders and
        // deadlock. Low probability — same index, same plan — but free to eliminate.
        // Requires MessageId : IComparable<MessageId>; a positional record derives no ordering.
        candidateIds.Sort();

        // 2. Stamp. The eligibility predicate is re-evaluated inside the UPDATE, so a row
        //    another processor claimed between step 1 and step 2 is simply not matched.
        //    This is the atomic part: whoever's UPDATE lands first owns the row.
        var claimedCount = await context.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id))
            .Where(EligibilityPredicate(now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Processing)
                .SetProperty(m => m.LockedUntilUtc, lockExpiry)
                .SetProperty(m => m.ClaimToken, token),
                ct)
            .ConfigureAwait(false);

        if (claimedCount == 0)
        {
            return OutboxClaim.Empty;
        }

        // 3. Read back — skipped entirely when nothing was contended, which is the normal
        //    case. Winning every candidate means the rows we already hold ARE the claim, so
        //    the third round trip buys nothing: bring the in-memory copies in line with what
        //    the UPDATE just wrote and return them. They are untracked (see Dispatchable), so
        //    mutating them cannot leak into the change tracker.
        if (claimedCount == candidates.Count)
        {
            foreach (var message in candidates)
            {
                message.Status = OutboxMessageStatus.Processing;
                message.LockedUntilUtc = lockExpiry;
                message.ClaimToken = token;
            }

            return new OutboxClaim(token, candidates);
        }

        // Only a partial win costs the extra round trip: some candidates went to another
        // processor and only the token can say which are ours.
        var claimed = await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimToken == token)
            // Must match the candidate query's sort above, for the reason stated there.
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new OutboxClaim(token, claimed);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Wrapped in an explicit transaction. Each <c>ExecuteUpdateAsync</c> otherwise runs in
    /// its own implicit transaction, so a database fault partway through would leave some
    /// messages settled and others still leased — a partial settlement that at-least-once
    /// delivery recovers from, but that is worth not producing in the first place.
    /// </para>
    /// <para>
    /// <b>Run as one operation of the context's execution strategy</b>
    /// (<see cref="SettlementTransaction"/>). A retrying strategy (<c>EnableRetryOnFailure</c>)
    /// refuses a transaction begun outside it, so every settlement would throw: no row would ever be
    /// marked <c>Published</c> or counted, and each would be re-sent on every lease expiry. A commit
    /// whose outcome is unknown is verified by the claim token, which the commit clears.
    /// </para>
    /// </remarks>
    public ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct = default)
        => SettlementTransaction.RunAsync(
            context,
            (store: this, claimToken, outcomes),
            static (s, token) => s.store.ApplyOutcomesCoreAsync(s.claimToken, s.outcomes, token),
            static (s, token) => s.store.IsSettledAsync(s.claimToken, s.outcomes, token),
            ct);

    /// <summary>Whether no message of <paramref name="outcomes"/> still carries the claim token.</summary>
    private async ValueTask<bool> IsSettledAsync(
        Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct)
    {
        var stillClaimed = await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimToken == claimToken)
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return !outcomes.Any(o => stillClaimed.Contains(o.MessageId));
    }

    private async ValueTask<int> ApplyOutcomesCoreAsync(
        Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var written = 0;

        // Grouped by disposition: two set-based statements cover the bulk, whatever the batch
        // size, instead of one statement per message. Every statement filters on the claim
        // token, so a lost lease yields zero rows rather than a silent overwrite.
        written += await SettleAsync(
            claimToken, outcomes, OutboxOutcomeKind.Published,
            s => s.SetProperty(m => m.Status, OutboxMessageStatus.Published)
                  .SetProperty(m => m.ProcessedAtUtc, now)
                  .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                  .SetProperty(m => m.ClaimToken, (Guid?)null),
            ct).ConfigureAwait(false);

        written += await SettleAsync(
            claimToken, outcomes, OutboxOutcomeKind.Released,
            s => s.SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                  .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                  .SetProperty(m => m.ClaimToken, (Guid?)null),
            ct).ConfigureAwait(false);

        written += await SettleDeferredAsync(claimToken, outcomes, ct).ConfigureAwait(false);

        // Retries and dead-letters carry per-message values (retry count, next attempt,
        // error text), so they cannot collapse into one statement without a server-side join
        // over a values list — which is provider-specific and would forfeit SQLite testing.
        // The cost is bounded by the number of FAILED messages, not by batch size: in normal
        // operation this loop does not execute at all.
        foreach (var outcome in outcomes)
        {
            switch (outcome.Kind)
            {
                case OutboxOutcomeKind.Retry:
                    written += await context.Set<OutboxMessage>()
                        .Where(m => m.Id == outcome.MessageId && m.ClaimToken == claimToken)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                            .SetProperty(m => m.RetryCount, outcome.RetryCount)
                            .SetProperty(m => m.NextRetryAtUtc, outcome.NextRetryAtUtc)
                            .SetProperty(m => m.ErrorMessage, outcome.ErrorMessage)
                            .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                            .SetProperty(m => m.ClaimToken, (Guid?)null),
                            ct)
                        .ConfigureAwait(false);
                    break;

                case OutboxOutcomeKind.DeadLetter:
                    written += await context.Set<OutboxMessage>()
                        .Where(m => m.Id == outcome.MessageId && m.ClaimToken == claimToken)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.Status, OutboxMessageStatus.Failed)
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

    // ---------- IOutboxAdminStore ----------

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(
        int batchSize, string? tenantId = null, CancellationToken ct = default)
    {
        // tenantId null means "every tenant". The previous signature took a non-nullable
        // string, so single-tenant deployments — where every row has a null TenantId —
        // never matched anything.
        return await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.DeadLettered)
            .Where(m => tenantId == null || m.TenantId == tenantId)
            // Deliberately OccurredOnUtc, unlike the claim: an operator triaging a dead-letter
            // queue is asking when the business fact happened, not where the row sat in a queue
            // that is no longer running. Harmonising the two would break one of the two purposes.
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RequeueAsync(MessageId id, CancellationToken ct = default)
    {
        var rows = await context.Set<OutboxMessage>()
            .Where(m => m.Id == id && m.DeadLettered)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                .SetProperty(m => m.DeadLettered, false)
                .SetProperty(m => m.RetryCount, 0)
                .SetProperty(m => m.NextRetryAtUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ErrorMessage, (string?)null)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ClaimToken, (Guid?)null),
                ct)
            .ConfigureAwait(false);

        // Returns the affected-row count rather than an unconditional success: zero rows
        // means the message was not dead-lettered, which the caller must be able to see.
        return rows == 1;
    }

    // ---------- IOutboxRetentionStore ----------

    /// <inheritdoc/>
    public async ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string? tenantId = null, CancellationToken ct = default)
    {
        return await context.Set<OutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Published && m.ProcessedAtUtc < olderThan)
            .Where(m => tenantId == null || m.TenantId == tenantId)

            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    // ---------- helpers ----------

    /// <summary>Leaves out the rows of the endpoints in back-off (#188).</summary>
    private static IQueryable<OutboxMessage> Excluding(IQueryable<OutboxMessage> rows, OutboxEndpoints excluded)
    {
        if ((excluded & OutboxEndpoints.Transport) != 0)
        {
            rows = rows.Where(m => m.Endpoint != null);
        }

        if ((excluded & OutboxEndpoints.Local) != 0)
        {
            // Three ranges around the local rows — the transport rows, and any other endpoint's,
            // which the processor dead-letters — as a UNION ALL of branches, each of which an index
            // can scan: "<> 'local'", or an OR the planner cannot push into the eligibility
            // predicate's own OR, walks the local backlog (EndpointExclusionPlanTests).
            // string.Compare is the form EF Core translates to SQL "<" and ">"; it is never run in
            // .NET, so the culture CA1309 guards against does not apply. The ranges equal
            // "<> 'local'" under a deterministic (case-sensitive) collation, the default of the tested
            // providers; under a case-insensitive one, an unroutable "LOCAL" would wait out the local
            // back-off before being dead-lettered.
#pragma warning disable CA1309
            rows = (excluded & OutboxEndpoints.Transport) != 0
                ? rows.Where(m => string.Compare(m.Endpoint, OutboxMessage.LocalEndpoint) < 0)
                    .Concat(rows.Where(m => string.Compare(m.Endpoint, OutboxMessage.LocalEndpoint) > 0))
                : rows.Where(m => m.Endpoint == null)
                    .Concat(rows.Where(m => string.Compare(m.Endpoint, OutboxMessage.LocalEndpoint) < 0))
                    .Concat(rows.Where(m => string.Compare(m.Endpoint, OutboxMessage.LocalEndpoint) > 0));
#pragma warning restore CA1309
        }

        return rows;
    }

    private IQueryable<OutboxMessage> Dispatchable(DateTimeOffset now) =>
        context.Set<OutboxMessage>().AsNoTracking().Where(EligibilityPredicate(now));

    /// <summary>
    /// A message is dispatchable when it is pending — or processing with an expired lease,
    /// which is how a crashed processor's work is recovered — is not dead-lettered, and
    /// has reached its back-off deadline.
    /// </summary>
    private static Expression<Func<OutboxMessage, bool>> EligibilityPredicate(DateTimeOffset now) =>
        m => !m.DeadLettered
             && (m.Status == OutboxMessageStatus.Pending
                 || (m.Status == OutboxMessageStatus.Processing && m.LockedUntilUtc <= now))
             && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now);

    /// <summary>
    /// Deferrals, set-based: an outage defers whole batches, so one statement per distinct next
    /// attempt — in practice one per down endpoint — rather than one per row. The retry count and
    /// the error text are left alone: no attempt was made.
    /// </summary>
    private async ValueTask<int> SettleDeferredAsync(
        Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct)
    {
        // A deferral with no next attempt (the positional constructor allows one) is settled as
        // eligible at once, like a release: never left claimed until its lease expires.
        var groups = new Dictionary<DateTimeOffset, List<MessageId>>();
        List<MessageId>? dueNow = null;
        foreach (var outcome in outcomes)
        {
            if (outcome.Kind is not OutboxOutcomeKind.Deferred)
            {
                continue;
            }

            List<MessageId>? ids;
            if (outcome.NextRetryAtUtc is not { } until)
            {
                ids = dueNow ??= [];
            }
            else if (!groups.TryGetValue(until, out ids))
            {
                groups[until] = ids = [];
            }

            ids.Add(outcome.MessageId);
        }

        var written = dueNow is null ? 0 : await DeferAsync(claimToken, dueNow, null, ct).ConfigureAwait(false);
        foreach (var (until, ids) in groups)
        {
            written += await DeferAsync(claimToken, ids, until, ct).ConfigureAwait(false);
        }

        return written;
    }

    private async ValueTask<int> DeferAsync(
        Guid claimToken, List<MessageId> ids, DateTimeOffset? nextAttempt, CancellationToken ct)
        => await context.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id) && m.ClaimToken == claimToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                .SetProperty(m => m.NextRetryAtUtc, nextAttempt)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ClaimToken, (Guid?)null),
                ct)
            .ConfigureAwait(false);

    private async ValueTask<int> SettleAsync(
        Guid claimToken,
        IReadOnlyList<OutboxOutcome> outcomes,
        OutboxOutcomeKind kind,
        Action<UpdateSettersBuilder<OutboxMessage>> setters,
        CancellationToken ct)
    {
        var ids = outcomes.Where(o => o.Kind == kind).Select(o => o.MessageId).ToList();

        if (ids.Count == 0)
        {
            return 0;
        }

        return await context.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id) && m.ClaimToken == claimToken)
            .ExecuteUpdateAsync(setters, ct)
            .ConfigureAwait(false);
    }
}
