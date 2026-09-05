namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The absorption of a replayed publication, against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// <para>
/// SQLite proves the writer stages and rolls back; it does not reproduce the behaviour this suite
/// exists for. On PostgreSQL a constraint violation aborts the <b>entire transaction</b>, so every
/// statement after it fails until a rollback — including the writer's own verification query, and
/// including everything the caller does afterwards. A bare <c>catch (DbUpdateException)</c> passes
/// on SQLite and destroys the caller's transaction here.
/// </para>
/// <para>
/// These drive <see cref="EfIntegrationEventWriter{TContext}"/> itself rather than a copy of its
/// body. A test that reimplements the mechanism it is meant to verify is green for the wrong
/// reason: it would keep passing after the shipped writer stopped taking a savepoint at all.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class IntegrationEventPublishingPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private const string Contract = "saasbtp.safety.constat-recorded.v1";

    /// <summary>
    /// A replayed dispatch republishes the same contract from the same origin. The writer reports
    /// it and — the part only PostgreSQL can prove — leaves the transaction usable.
    /// </summary>
    [DockerRequiredFact]
    public async Task Replay_IsAbsorbedAndTheTransactionRemainsUsable()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        var origin = MessageId.New();

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var writer = new EfIntegrationEventWriter<TestMessagingDbContext>(context);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var first = await writer.AddAsync(ContractRow(origin));
        first.AlreadyPublished.ShouldBeFalse();

        var replay = await writer.AddAsync(ContractRow(origin));

        replay.AlreadyPublished.ShouldBeTrue("the replay key rejected the second write");
        replay.Id.ShouldBe(first.Id, "the id returned must name the row that actually exists");

        // Without the savepoint this next statement fails with
        // "current transaction is aborted, commands ignored until end of transaction block".
        var unrelated = await writer.AddAsync(ContractRow(MessageId.New()));
        unrelated.AlreadyPublished.ShouldBeFalse();

        await transaction.CommitAsync();

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        (await probe.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);
    }

    /// <summary>
    /// The caller's own pending writes survive an absorbed duplicate — with EF's automatic
    /// savepoints ON.
    /// </summary>
    /// <remarks>
    /// <b>Both this and its <c>WithoutAutoSavepoints</c> twin are required, and neither is
    /// redundant.</b> The writer's flush is not partial: it writes every change tracked on the
    /// caller's context, and the savepoint rollback on the absorbed path unwinds those rows too.
    /// The mechanism is only safe because EF leaves them <c>Added</c>/<c>Modified</c> after a
    /// failed save — it calls <c>AcceptAllChanges</c> only on success — so the caller's own
    /// <c>CommitAsync</c> rewrites them.
    /// <para>
    /// In THIS configuration EF creates its own savepoint around <c>SaveChanges</c> and rolls back
    /// to it, so the writer's explicit savepoint is belt and braces and the test passes without it.
    /// A reader who sees only this case will conclude the explicit savepoint is dead code. It is
    /// not: see the twin, which is the case that fails without it. Verified by mutation, not
    /// assumed — deleting <c>RollbackToSavepointAsync</c> from the writer fails the twin with
    /// PostgreSQL <c>25P02</c> and leaves this one green.
    /// </para>
    /// </remarks>
    [DockerRequiredFact]
    public Task AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact_WithAutoSavepoints()
        => AbsorbedDuplicateLeavesTheCallersOwnWritesIntactAsync(autoSavepoints: true);

    /// <summary>
    /// The same property with EF's automatic savepoints OFF — an ordinary setting on a consumer's
    /// context, and the configuration in which the writer's own savepoint is load-bearing.
    /// </summary>
    /// <remarks>
    /// This is the case that fails if the writer stops taking a savepoint: the verification query
    /// inside its catch block runs on a transaction PostgreSQL has already aborted. Making the
    /// guarantee independent of a consumer setting the library cannot control is the point — the
    /// same reasoning <see cref="EfInboxStore{TContext}.AddAsync"/> records.
    /// </remarks>
    [DockerRequiredFact]
    public Task AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact_WithoutAutoSavepoints()
        => AbsorbedDuplicateLeavesTheCallersOwnWritesIntactAsync(autoSavepoints: false);

    /// <summary>
    /// A publication outside a dispatch carries no origin, and nulls are distinct in the unique
    /// index — so two of them coexist rather than deduplicating.
    /// </summary>
    /// <remarks>
    /// The intended scope of the replay key, not a gap: it guards the replay of a dispatch, and
    /// outside one there is nothing to replay. It also guards the writer's verification query,
    /// which must never run for a null origin — <c>OriginMessageId == null</c> translates to
    /// <c>IS NULL</c> and would match an unrelated row.
    /// </remarks>
    [DockerRequiredFact]
    public async Task Publish_WithNoOrigin_DoesNotDeduplicate()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var writer = new EfIntegrationEventWriter<TestMessagingDbContext>(context);

        await using var transaction = await context.Database.BeginTransactionAsync();

        (await writer.AddAsync(ContractRow(origin: null))).AlreadyPublished.ShouldBeFalse();
        (await writer.AddAsync(ContractRow(origin: null))).AlreadyPublished.ShouldBeFalse();

        await transaction.CommitAsync();

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        (await probe.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);
    }

    /// <summary>
    /// A write that fails for any reason other than the replay key must still throw.
    /// </summary>
    /// <remarks>
    /// Uses a NOT NULL violation deliberately, mirroring the inbox's test of the same shape.
    /// Absorbing a fault that is not the dedup gate would trade a visible error for silent data
    /// loss, which is strictly worse.
    /// </remarks>
    [DockerRequiredFact]
    public async Task A_non_duplicate_write_failure_still_throws()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var writer = new EfIntegrationEventWriter<TestMessagingDbContext>(context);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var malformed = ContractRow(MessageId.New());
        malformed.EventType = null!;

        await Should.ThrowAsync<DbUpdateException>(async () => await writer.AddAsync(malformed));
    }

    /// <summary>
    /// The retention guard's correlated subquery translates on Npgsql, and does what it says.
    /// </summary>
    /// <remarks>
    /// SQLite translating it proves nothing about the provider anyone deploys — the same reasoning
    /// behind <c>ClaimBatchAsync_TranslatesTheMessageIdContainsFilter</c>. An untranslatable
    /// predicate throws at run time on the retention worker's timer, where it would surface as a
    /// background exception hours after deployment rather than as a failing test.
    /// </remarks>
    [DockerRequiredFact]
    public async Task DeleteProcessedAsync_TranslatesTheOriginGuard_AndHonoursIt()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        var deadLetteredOrigin = ContractRow(origin: null);
        deadLetteredOrigin.MessageKind = MessageKind.Notification;
        deadLetteredOrigin.ContractName = null;
        deadLetteredOrigin.Source = null;
        deadLetteredOrigin.Status = OutboxMessageStatus.Failed;
        deadLetteredOrigin.DeadLettered = true;

        var held = ContractRow(deadLetteredOrigin.Id);
        held.Status = OutboxMessageStatus.Published;
        held.ProcessedAtUtc = Now.AddDays(-30);

        var publishedOrigin = ContractRow(origin: null);
        publishedOrigin.MessageKind = MessageKind.Notification;
        publishedOrigin.ContractName = null;
        publishedOrigin.Source = null;
        publishedOrigin.Status = OutboxMessageStatus.Published;
        publishedOrigin.ProcessedAtUtc = Now.AddDays(-30);

        var free = ContractRow(publishedOrigin.Id);
        free.ContractName = "saasbtp.safety.other.v1";
        free.Status = OutboxMessageStatus.Published;
        free.ProcessedAtUtc = Now.AddDays(-30);

        await using (var seed = InboxTestDatabase.NewContext(schema.ConnectionString))
        {
            seed.OutboxMessages.AddRange(deadLetteredOrigin, held, publishedOrigin, free);
            await seed.SaveChangesAsync();
        }

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var store = new EfOutboxStore<TestMessagingDbContext>(
            context, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));

        var deleted = await store.DeleteProcessedAsync(Now.AddDays(-7));

        deleted.ShouldBe(2, "the published origin and the contract it no longer protects");

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        var survivors = await probe.OutboxMessages.AsNoTracking().Select(m => m.Id).ToListAsync();

        survivors.ShouldContain(held.Id, "its origin is dead-lettered and can still be requeued");
        survivors.ShouldContain(deadLetteredOrigin.Id, "a dead-lettered row is never purged");
    }

    private async Task AbsorbedDuplicateLeavesTheCallersOwnWritesIntactAsync(bool autoSavepoints)
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        var origin = MessageId.New();

        // The first publication, already committed. This is what a replay collides with.
        await using (var seed = InboxTestDatabase.NewContext(schema.ConnectionString))
        {
            seed.OutboxMessages.Add(ContractRow(origin));
            await seed.SaveChangesAsync();
        }

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        context.Database.AutoSavepointsEnabled = autoSavepoints;

        var writer = new EfIntegrationEventWriter<TestMessagingDbContext>(context);

        await using var transaction = await context.Database.BeginTransactionAsync();

        // The caller's own pending write, staged before the publication — the change set the
        // writer's flush carries along on the caller's behalf.
        var business = NewInboxMessage();
        context.InboxMessages.Add(business);

        var result = await writer.AddAsync(ContractRow(origin));

        result.AlreadyPublished.ShouldBeTrue();

        context.Entry(business).State.ShouldBe(
            EntityState.Added,
            "THE CLAIM THE FLUSH RESTS ON: a failed SaveChanges must leave the caller's pending " +
            "entity Added, or the savepoint rollback silently discards the handler's own writes");

        // The caller's CommitAsync.
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);

        (await probe.InboxMessages.AsNoTracking().CountAsync(m => m.RowId == business.RowId))
            .ShouldBe(1, "the caller's own write must survive an absorbed duplicate");
        (await probe.OutboxMessages.AsNoTracking().CountAsync())
            .ShouldBe(1, "the duplicate must not have been written");
    }

    private static OutboxMessage ContractRow(MessageId? origin)
        => new()
        {
            Id = MessageId.New(),
            TenantId = "tenant-a",
            MessageKind = MessageKind.Contract,
            ContractName = Contract,
            Source = "/saasbtp/safety",
            OriginMessageId = origin,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            OccurredOnUtc = Now,
            CreatedAtUtc = Now,
            CorrelationId = CorrelationId.New(),
        };

    private static InboxMessage NewInboxMessage()
        => new()
        {
            RowId = Guid.NewGuid(),
            MessageId = MessageId.New(),
            ConsumerType = "OrderPlacedHandler",
            TenantId = "tenant-a",
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = InboxMessageStatus.Received,
            ReceivedAtUtc = Now,
        };
}
