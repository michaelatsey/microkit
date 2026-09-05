namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>EF Core staging writer for integration events.</summary>
/// <remarks>
/// <para>
/// Writes a <see cref="MessageKind.Contract"/> row into the outbox inside the transaction the
/// caller already opened. It does not commit: the caller's unit of work owns that boundary, and a
/// rollback still erases the row, so nothing is announced for a fact that did not happen.
/// </para>
/// <para>
/// <b>Why this one flushes when <c>EfOutboxStore.AddAsync</c> does not.</b> The replay key on
/// (<see cref="OutboxMessage.OriginMessageId"/>, <see cref="OutboxMessage.ContractName"/>) can only
/// be consulted by attempting the insert, and the answer has to reach
/// <c>IIntegrationEventPublisher</c> before it returns — otherwise the violation surfaces later,
/// out of the caller's own <c>CommitAsync</c>, where nothing can absorb it and a notification
/// handler would have to catch a database exception to survive its own replay. Tracking the entity
/// and leaving it to the caller's save, which is what the domain-event path does, puts the answer
/// permanently out of reach.
/// </para>
/// <para>
/// <b>The flush is not partial, and that is the accepted cost.</b> EF Core has no per-entity save,
/// so <c>SaveChangesAsync</c> writes every change tracked on this context — which is the caller's
/// context, by the scope-identity guarantee <c>AddEfCoreIntegrationEvents</c> rests on. Inside one
/// transaction that changes no outcome: same final state, same rollback semantics, and the caller's
/// later <c>CommitAsync</c> simply finds those entries already written. What it changes is
/// <i>when</i> a constraint violation in the caller's own change set surfaces — at the publish
/// rather than at the commit.
/// </para>
/// <para>
/// The mechanism depends on EF leaving the caller's entities <c>Added</c>/<c>Modified</c> after a
/// failed save, so the savepoint rollback below does not silently discard them. It does — EF calls
/// <c>AcceptAllChanges</c> only on success — and that is asserted against a real server by
/// <c>AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact</c> rather than taken from documentation.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfIntegrationEventWriter<TContext>(TContext context) : IIntegrationEventWriter
    where TContext : DbContext
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>How the duplicate is recognised.</b> Not by decoding a provider error code. The insert is
    /// attempted, and if it fails the row is queried: if the contract is there now under this
    /// origin, the replay key held and this publication already happened. That asks the question
    /// the decision depends on — "is this contract already staged?" — rather than the syntactic one
    /// a detector answers, "was that error a unique violation?". It costs nothing in provider
    /// knowledge and keeps working on providers that do not exist yet. It is a check <i>after</i>
    /// the failed insert, never a guard before it, so the unique index remains the sole authority
    /// and there is no time-of-check-to-time-of-use window. The same reasoning as
    /// <see cref="EfInboxStore{TContext}.AddAsync"/>, and the same rejected alternatives: EF Core 10
    /// exposes no typed constraint exception, <c>EntityFramework.Exceptions</c> activates through a
    /// setting on the <b>consumer's</b> context that a library cannot make, and a hand-written
    /// <c>SqlState</c> detector does not survive a deferred constraint on PostgreSQL.
    /// </para>
    /// <para>
    /// <b>Why the savepoint.</b> On PostgreSQL a constraint violation aborts the entire transaction;
    /// every later statement fails until a rollback — <b>including the verification query in this
    /// very catch block</b>, and including everything the caller does afterwards. Note a
    /// per-provider detector would not have helped: it classifies the exception correctly and then
    /// returns into a transaction that is already dead.
    /// </para>
    /// <para>
    /// EF creates its own automatic savepoint around <c>SaveChanges</c> inside a manually-started
    /// transaction, so in the default configuration this one is belt and braces. It becomes
    /// load-bearing when a consumer sets <c>Database.AutoSavepointsEnabled = false</c>, and making
    /// the guarantee independent of a consumer setting is the point. Both configurations are
    /// exercised, because only running both distinguishes them.
    /// </para>
    /// <para>
    /// <b>Provider limits.</b> The savepoint name is kept to 24 characters because SQL Server caps
    /// savepoint identifiers at 32 and rejects longer ones; where a name is truncated rather than
    /// rejected, two savepoints can share a name and a rollback unwinds to the wrong one without
    /// complaint. SQL Server is not a supported provider for the replay key at all — it compares
    /// nulls as equal, so the second notification row ever written would be rejected — which makes
    /// the cap inert here rather than wrong. It is kept because it costs one slice and rediscovering
    /// it costs an incident.
    /// </para>
    /// <para>
    /// <b>Known gap.</b> If retention deletes the pre-existing row between the failed insert and the
    /// verification, the duplicate is rethrown as a fault. Vanishingly rare, and it fails in the
    /// safe direction: a visible error rather than a silent loss.
    /// </para>
    /// </remarks>
    public async ValueTask<IntegrationEventWriteResult> AddAsync(
        OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // No null branch, unlike EfInboxStore: IIntegrationEventPublisher refuses to publish
        // without an open transaction, so by the time this runs there is always one. Asserted
        // rather than branched, so a future caller that skips the guard fails here loudly instead
        // of quietly writing an uncommittable row through an implicit transaction.
        var ambient = context.Database.CurrentTransaction
            ?? throw new IntegrationEventPublishException(
                $"'{message.ContractName}' reached the staging writer with no open transaction. " +
                "IIntegrationEventPublisher guards against this before staging; a caller that " +
                "bypassed the publisher must open a transaction itself.");

        if (!ambient.SupportsSavepoints)
        {
            // Proceeding would leave the caller's transaction unusable after a duplicate, and a
            // duplicate is the nominal path here. Refusing loudly beats corrupting a transaction
            // the caller believes is healthy.
            throw new IntegrationEventPublishException(
                "Integration event publishing is running on a provider that does not support " +
                "savepoints. A replayed publication would abort the caller's transaction with no " +
                "way to recover. Use a provider with savepoint support.");
        }

        // 24 characters — see the remarks. 17 hex characters is ample entropy for a name that only
        // has to be unique inside one transaction.
        var savepoint = $"mk_ieo_{Guid.NewGuid():N}"[..24];
        await ambient.CreateSavepointAsync(savepoint, ct).ConfigureAwait(false);

        var entry = context.Set<OutboxMessage>().Add(message);

        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);

            // Released on success so a handler publishing several contracts does not accumulate
            // one subtransaction per event.
            await ambient.ReleaseSavepointAsync(savepoint, ct).ConfigureAwait(false);

            return IntegrationEventWriteResult.Staged(message.Id);
        }
        catch (DbUpdateException)
        {
            // Detach first: the rejected row would otherwise stay tracked as added, and the
            // caller's own CommitAsync would retry the same failing insert — so one absorbed
            // replay would break the commit it was absorbed to protect.
            entry.State = EntityState.Detached;

            // Must precede any other statement: on PostgreSQL the connection refuses everything
            // until the aborted transaction is unwound. This also restores whatever the caller had
            // pending, which EF left Added/Modified because the save failed.
            await ambient.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);

            if (message.OriginMessageId is null)
            {
                // Nothing to verify against. A null origin is DISTINCT from every other null in the
                // unique index, so this row cannot have collided on the replay key — whatever
                // failed was something else, and absorbing it would be silent data loss.
                throw;
            }

            var origin = message.OriginMessageId;
            var contractName = message.ContractName;

            var existing = await context.Set<OutboxMessage>()
                .AsNoTracking()
                .Where(m => m.OriginMessageId == origin && m.ContractName == contractName)
                .Select(m => m.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return IntegrationEventWriteResult.AlreadyPublishedAs(existing);
            }

            // The contract is not there, so whatever failed was not the replay key. Absorbing this
            // would trade a spurious retry for silent data loss, which is worse.
            throw;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reflects the context <i>this writer</i> holds. If the caller opens its transaction on a
    /// different <c>DbContext</c> instance, the guard passes while the row commits somewhere else —
    /// the same scope-identity assumption <c>IInboxSettlementStore</c> rests on, and the reason
    /// that identity is pinned by an integration test rather than trusted to a comment.
    /// </remarks>
    public bool HasOpenTransaction => context.Database.CurrentTransaction is not null;
}
