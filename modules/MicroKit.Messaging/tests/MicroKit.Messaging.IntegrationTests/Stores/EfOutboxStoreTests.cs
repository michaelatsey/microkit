using MicroKit.Messaging.Options;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// The outbox store contract under SQLite. The claim carries no provider-specific SQL, so the
/// production code path is exactly what runs here.
/// </summary>
public sealed class EfOutboxStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    // Helper: isolated SQLite connection per test (ADR-MSG testing rule).
    private static (SqliteConnection conn, TestMessagingDbContext ctx) CreateIsolatedDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var ctx = new TestMessagingDbContext(
            new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return (conn, ctx);
    }

    private static TestMessagingDbContext SecondContext(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);

    private static EfOutboxStore<TestMessagingDbContext> Store(
        TestMessagingDbContext ctx, DateTimeOffset? now = null)
        => new(ctx, new FakeTimeProvider(now ?? Now));

    private static OutboxMessage BuildOutboxMessage(
        OutboxMessageStatus status = OutboxMessageStatus.Pending,
        DateTimeOffset? lockedUntilUtc = null,
        DateTimeOffset? nextRetryAtUtc = null,
        bool deadLettered = false,
        string? tenantId = "tenant-a",
        DateTimeOffset? occurredOnUtc = null,
        DateTimeOffset? processedAtUtc = null,
        Guid? claimToken = null,
        MessageKind messageKind = MessageKind.Notification,
        string? contractName = null,
        string? source = null,
        MessageId? sourceMessageId = null)
    {
        return new OutboxMessage
        {
            Id = MessageId.New(),
            TenantId = tenantId,
            MessageKind = messageKind,
            ContractName = contractName,
            Source = source,
            SourceMessageId = sourceMessageId,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = status,
            OccurredOnUtc = occurredOnUtc ?? Now,
            CreatedAtUtc = Now,
            CorrelationId = CorrelationId.New(),
            LockedUntilUtc = lockedUntilUtc,
            NextRetryAtUtc = nextRetryAtUtc,
            ProcessedAtUtc = processedAtUtc,
            DeadLettered = deadLettered,
            ClaimToken = claimToken,
        };
    }

    // ---------------------------------------------------------------------------
    // IOutboxWriter
    // ---------------------------------------------------------------------------

    [Fact]
    public Task AddAsync_StagesMessage_NotPersistedUntilSaveChanges()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            await Store(ctx).AddAsync(BuildOutboxMessage());

            await using (var probe = SecondContext(conn))
            {
                (await probe.OutboxMessages.CountAsync()).ShouldBe(0);
            }

            await ctx.SaveChangesAsync();

            await using var after = SecondContext(conn);
            (await after.OutboxMessages.CountAsync()).ShouldBe(1);
        });

    /// <summary>
    /// The columns a message is addressed by survive a write and a read on a real provider.
    /// </summary>
    /// <remarks>
    /// Read back through a SECOND context, so the assertion cannot be satisfied by the change
    /// tracker still holding the instance that was written — which is what an unmapped property
    /// would do: <c>Source</c> missing from the configuration produces a green in-memory assertion
    /// and a null column.
    /// </remarks>
    [Fact]
    public Task AddAsync_RoundTripsTheContractAddressingColumns()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var sourceRow = MessageId.New();
            var message = BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "shop.orders.order-placed.v1",
                source: "/shop/orders",
                sourceMessageId: sourceRow);

            await Store(ctx).AddAsync(message);
            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            var stored = await probe.OutboxMessages.SingleAsync();

            stored.MessageKind.ShouldBe(MessageKind.Contract);
            stored.ContractName.ShouldBe("shop.orders.order-placed.v1");
            stored.Source.ShouldBe("/shop/orders");
            stored.SourceMessageId.ShouldBe(sourceRow);
        });

    /// <summary>
    /// A notification row stores null in both wire columns, and the schema permits it.
    /// </summary>
    /// <remarks>
    /// Not a trivial mirror of the test above: marking <c>Source</c> or <c>ContractName</c>
    /// <c>IsRequired()</c> would break every notification write in the module, and this is what
    /// would catch it. It also covers the unique index over
    /// <c>(SourceMessageId, ContractName)</c> — two all-null rows must coexist, which they do on
    /// SQLite and PostgreSQL because nulls are distinct there.
    /// </remarks>
    [Fact]
    public Task AddAsync_AllowsNullWireColumnsOnNotificationRows()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            await Store(ctx).AddBatchAsync(
                [BuildOutboxMessage(), BuildOutboxMessage()]);
            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            var stored = await probe.OutboxMessages.ToListAsync();

            stored.Count.ShouldBe(2);
            stored.ShouldAllBe(m => m.Source == null);
            stored.ShouldAllBe(m => m.ContractName == null);
        });

    [Fact]
    public Task AddBatchAsync_StagesEveryMessage_InOneSaveChanges()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            await Store(ctx).AddBatchAsync([BuildOutboxMessage(), BuildOutboxMessage(), BuildOutboxMessage()]);
            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            (await probe.OutboxMessages.CountAsync()).ShouldBe(3);
        });

    // ---------------------------------------------------------------------------
    // ClaimBatchAsync — eligibility
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ClaimBatchAsync_ReturnsOnlyEligibleMessages()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var pending = BuildOutboxMessage();
            ctx.OutboxMessages.AddRange(pending, BuildOutboxMessage(OutboxMessageStatus.Published));
            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(10, Lease);

            claim.Count.ShouldBe(1);
            claim.Messages[0].Id.ShouldBe(pending.Id);
        });

    [Fact]
    public Task ClaimBatchAsync_IncludesExpiredLease_RecoveringACrashedProcessor()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var stale = BuildOutboxMessage(
                OutboxMessageStatus.Processing,
                lockedUntilUtc: Now.AddMinutes(-1),
                claimToken: Guid.NewGuid());
            ctx.OutboxMessages.Add(stale);
            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(10, Lease);

            claim.Count.ShouldBe(1);
            claim.Messages[0].ClaimToken.ShouldBe(claim.Token, "the new owner's token replaces the dead one");
        });

    [Fact]
    public Task ClaimBatchAsync_ExcludesLiveLease()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage(
                OutboxMessageStatus.Processing, lockedUntilUtc: Now.AddMinutes(5)));
            await ctx.SaveChangesAsync();

            (await Store(ctx).ClaimBatchAsync(10, Lease)).Count.ShouldBe(0);
        });

    [Fact]
    public Task ClaimBatchAsync_ExcludesMessageWithFutureNextRetryAtUtc()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage(nextRetryAtUtc: Now.AddMinutes(5)));
            await ctx.SaveChangesAsync();

            (await Store(ctx).ClaimBatchAsync(10, Lease)).Count.ShouldBe(0);
        });

    [Fact]
    public Task ClaimBatchAsync_ExcludesDeadLettered()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true));
            await ctx.SaveChangesAsync();

            (await Store(ctx).ClaimBatchAsync(10, Lease)).Count.ShouldBe(0);
        });

    [Fact]
    public Task ClaimBatchAsync_WhenNothingEligible_ReturnsEmptyClaim()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var claim = await Store(ctx).ClaimBatchAsync(10, Lease);

            claim.Count.ShouldBe(0);
            claim.Token.ShouldBe(Guid.Empty);
        });

    // ---------------------------------------------------------------------------
    // ClaimBatchAsync — stamping and ordering
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ClaimBatchAsync_StampsStatusLeaseAndTokenOnEveryClaimedRow()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(BuildOutboxMessage(), BuildOutboxMessage(), BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(10, Lease);

            claim.Count.ShouldBe(3);
            claim.Token.ShouldNotBe(Guid.Empty);

            await using var probe = SecondContext(conn);
            var rows = await probe.OutboxMessages.AsNoTracking().ToListAsync();

            rows.ShouldAllBe(m => m.Status == OutboxMessageStatus.Processing);
            rows.ShouldAllBe(m => m.ClaimToken == claim.Token);
            rows.ShouldAllBe(m => m.LockedUntilUtc == Now.Add(Lease));

            // The returned instances must agree with what the database now holds — the
            // uncontended path patches them in memory rather than re-reading.
            claim.Messages.ShouldAllBe(m => m.Status == OutboxMessageStatus.Processing);
            claim.Messages.ShouldAllBe(m => m.ClaimToken == claim.Token);
            claim.Messages.ShouldAllBe(m => m.LockedUntilUtc == Now.Add(Lease));
        });

    [Fact]
    public Task ClaimBatchAsync_RespectsBatchSize_AndClaimsOldestFirst()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var oldest = BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-30));
            var middle = BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-20));
            var newest = BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-10));
            ctx.OutboxMessages.AddRange(newest, oldest, middle);
            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(2, Lease);

            claim.Count.ShouldBe(2);
            claim.Messages.Select(m => m.Id).ShouldBe([oldest.Id, middle.Id]);
        });

    [Fact]
    public Task ClaimBatchAsync_WithMultipleCandidates_SortsIdsWithoutThrowing()
        => Task.Run(async () =>
        {
            // Regression guard: candidate ids are sorted before the stamp so concurrent
            // processors lock overlapping rows in the same order. MessageId is a positional
            // record, which derives no ordering — without IComparable<MessageId>, List.Sort
            // throws for any batch of two or more while passing for zero or one.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            for (var i = 0; i < 25; i++)
            {
                ctx.OutboxMessages.Add(BuildOutboxMessage());
            }

            await ctx.SaveChangesAsync();

            // Not wrapped in Should.NotThrowAsync: an InvalidOperationException from Sort()
            // escaping here fails the test just as loudly, and keeps the claim value usable.
            var claim = await Store(ctx).ClaimBatchAsync(25, Lease);

            claim.Count.ShouldBe(25);
        });

    [Fact]
    public Task ClaimBatchAsync_TwoProcessors_ShareNoMessageAndLoseNone()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            for (var i = 0; i < 10; i++)
            {
                ctx.OutboxMessages.Add(BuildOutboxMessage());
            }

            await ctx.SaveChangesAsync();

            await using var ctxB = SecondContext(conn);

            var claimA = await Store(ctx).ClaimBatchAsync(10, Lease);
            var claimB = await Store(ctxB).ClaimBatchAsync(10, Lease);

            var idsA = claimA.Messages.Select(m => m.Id).ToHashSet();
            var idsB = claimB.Messages.Select(m => m.Id).ToHashSet();

            idsA.Overlaps(idsB).ShouldBeFalse("two processors must never both win the same row");
            (idsA.Count + idsB.Count).ShouldBe(10, "and between them they must lose none");
            claimA.Token.ShouldNotBe(claimB.Token);
        });

    // ---------------------------------------------------------------------------
    // ApplyOutcomesAsync — dispositions
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ApplyOutcomesAsync_Published_SetsTerminalStateAndClearsTheLease()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var written = await store.ApplyOutcomesAsync(
                claim.Token, [OutboxOutcome.Published(claim.Messages[0].Id)]);

            written.ShouldBe(1);

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Published);
            row.ProcessedAtUtc.ShouldBe(Now);
            row.LockedUntilUtc.ShouldBeNull();
            row.ClaimToken.ShouldBeNull();
        });

    [Fact]
    public Task ApplyOutcomesAsync_Released_ReturnsToPendingWithoutConsumingRetries()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            await store.ApplyOutcomesAsync(claim.Token, [OutboxOutcome.Released(claim.Messages[0].Id)]);

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Pending);
            row.RetryCount.ShouldBe(0, "a released message never reached the transport");
            row.NextRetryAtUtc.ShouldBeNull("and must be immediately eligible again");
            row.LockedUntilUtc.ShouldBeNull();
            row.ClaimToken.ShouldBeNull();
        });

    [Fact]
    public Task ApplyOutcomesAsync_Retry_PersistsRetryCountAndNextAttempt()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var nextAttempt = Now.AddSeconds(4);

            await store.ApplyOutcomesAsync(
                claim.Token,
                [OutboxOutcome.Retry(claim.Messages[0].Id, 1, nextAttempt, "transient error")]);

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Pending);
            row.RetryCount.ShouldBe(1);
            row.NextRetryAtUtc.ShouldBe(nextAttempt);
            row.ErrorMessage.ShouldBe("transient error");
            row.LockedUntilUtc.ShouldBeNull();
            row.ClaimToken.ShouldBeNull();
        });

    [Fact]
    public Task ApplyOutcomesAsync_DeadLetter_SetsFailedAndDeadLettered()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);

            await store.ApplyOutcomesAsync(
                claim.Token, [OutboxOutcome.DeadLetter(claim.Messages[0].Id, 5, "max retries exceeded")]);

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Failed);
            row.DeadLettered.ShouldBeTrue();
            row.RetryCount.ShouldBe(5);
            row.ProcessedAtUtc.ShouldBe(Now);
            row.ErrorMessage.ShouldBe("max retries exceeded");
            row.ClaimToken.ShouldBeNull();
        });

    [Fact]
    public Task ApplyOutcomesAsync_MixedDispositions_SettlesEveryOneInOneCall()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(
                BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-4)),
                BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-3)),
                BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-2)),
                BuildOutboxMessage(occurredOnUtc: Now.AddMinutes(-1)));
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var ids = claim.Messages.Select(m => m.Id).ToList();

            var written = await store.ApplyOutcomesAsync(claim.Token,
            [
                OutboxOutcome.Published(ids[0]),
                OutboxOutcome.Retry(ids[1], 2, Now.AddSeconds(4), "boom"),
                OutboxOutcome.DeadLetter(ids[2], 5, "poison"),
                OutboxOutcome.Released(ids[3]),
            ]);

            written.ShouldBe(4);

            await using var probe = SecondContext(conn);
            var rows = await probe.OutboxMessages.AsNoTracking().ToDictionaryAsync(m => m.Id);
            rows[ids[0]].Status.ShouldBe(OutboxMessageStatus.Published);
            rows[ids[1]].Status.ShouldBe(OutboxMessageStatus.Pending);
            rows[ids[1]].RetryCount.ShouldBe(2);
            rows[ids[2]].DeadLettered.ShouldBeTrue();
            rows[ids[3]].Status.ShouldBe(OutboxMessageStatus.Pending);
            rows[ids[3]].RetryCount.ShouldBe(0);
        });

    // ---------------------------------------------------------------------------
    // The token guard — defect #1
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ApplyOutcomesAsync_WithStaleToken_WritesNothing()
        => Task.Run(async () =>
        {
            // A processor whose lease expired mid-dispatch must not overwrite the processor
            // that legitimately re-claimed its message. Filtering on the id alone let it.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var storeA = Store(ctx);
            var staleClaim = await storeA.ClaimBatchAsync(10, Lease);
            var messageId = staleClaim.Messages[0].Id;

            // The lease expires and processor B takes the message over.
            await using var ctxB = SecondContext(conn);
            var storeB = Store(ctxB, Now.Add(Lease).AddMinutes(1));
            var freshClaim = await storeB.ClaimBatchAsync(10, Lease);
            freshClaim.Count.ShouldBe(1, "the expired lease must be re-claimable");
            freshClaim.Token.ShouldNotBe(staleClaim.Token);

            // Processor A now finishes and tries to settle under its dead token.
            var written = await storeA.ApplyOutcomesAsync(
                staleClaim.Token, [OutboxOutcome.Published(messageId)]);

            written.ShouldBe(0, "a lost lease must yield zero rows, not a silent overwrite");

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Processing, "processor B still owns it");
            row.ClaimToken.ShouldBe(freshClaim.Token);
        });

    [Fact]
    public Task ApplyOutcomesAsync_WithStaleToken_RejectsFailureWritesToo()
        => Task.Run(async () =>
        {
            // Retry and DeadLetter are per-message statements; they carry the same guard.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var storeA = Store(ctx);
            var staleClaim = await storeA.ClaimBatchAsync(10, Lease);
            var messageId = staleClaim.Messages[0].Id;

            await using var ctxB = SecondContext(conn);
            await Store(ctxB, Now.Add(Lease).AddMinutes(1)).ClaimBatchAsync(10, Lease);

            var written = await storeA.ApplyOutcomesAsync(staleClaim.Token,
            [
                OutboxOutcome.Retry(messageId, 9, Now.AddHours(1), "stale retry"),
            ]);

            written.ShouldBe(0);

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.RetryCount.ShouldBe(0, "the stale processor must not bump another owner's retry count");
            row.ErrorMessage.ShouldBeNull();
        });

    [Fact]
    public Task ApplyOutcomesAsync_ReportsPartialSettlementViaRowCount()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(BuildOutboxMessage(), BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var ids = claim.Messages.Select(m => m.Id).ToList();

            // One of the two is stolen back before settlement.
            await using var ctxB = SecondContext(conn);
            await ctxB.OutboxMessages
                .Where(m => m.Id == ids[0])
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ClaimToken, (Guid?)null));

            var written = await store.ApplyOutcomesAsync(
                claim.Token, [OutboxOutcome.Published(ids[0]), OutboxOutcome.Published(ids[1])]);

            written.ShouldBe(1, "zero rows must be distinguishable from success");
        });

    // ---------------------------------------------------------------------------
    // IOutboxAdminStore
    // ---------------------------------------------------------------------------

    [Fact]
    public Task GetDeadLetteredAsync_WithNullTenant_ReturnsEveryTenantIncludingNullTenantRows()
        => Task.Run(async () =>
        {
            // Defect #3: the old signature took a non-nullable string, so single-tenant
            // deployments — where every row has a null TenantId — matched nothing at all.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(
                BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, tenantId: "tenant-a"),
                BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, tenantId: null),
                BuildOutboxMessage());
            await ctx.SaveChangesAsync();

            var result = await Store(ctx).GetDeadLetteredAsync(10);

            result.Count.ShouldBe(2);
        });

    [Fact]
    public Task GetDeadLetteredAsync_WithTenantFilter_NarrowsToThatTenant()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(
                BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, tenantId: "tenant-a"),
                BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, tenantId: "tenant-b"));
            await ctx.SaveChangesAsync();

            var result = await Store(ctx).GetDeadLetteredAsync(10, "tenant-a");

            result.Count.ShouldBe(1);
            result[0].TenantId.ShouldBe("tenant-a");
        });

    [Fact]
    public Task RequeueAsync_WhenDeadLettered_ResetsToPendingAndReturnsTrue()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var dl = BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true);
            dl.RetryCount = 5;
            dl.ErrorMessage = "poison";
            ctx.OutboxMessages.Add(dl);
            await ctx.SaveChangesAsync();

            (await Store(ctx).RequeueAsync(dl.Id)).ShouldBeTrue();

            await using var probe = SecondContext(conn);
            var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Pending);
            row.DeadLettered.ShouldBeFalse();
            row.RetryCount.ShouldBe(0);
            row.NextRetryAtUtc.ShouldBeNull();
            row.ErrorMessage.ShouldBeNull();
            row.ClaimToken.ShouldBeNull();
        });

    [Fact]
    public Task RequeueAsync_WhenNotDeadLettered_ReturnsFalse()
        => Task.Run(async () =>
        {
            // Silent success is forbidden: the caller must be able to see that nothing matched.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var pending = BuildOutboxMessage();
            ctx.OutboxMessages.Add(pending);
            await ctx.SaveChangesAsync();

            (await Store(ctx).RequeueAsync(pending.Id)).ShouldBeFalse();
        });

    // ---------------------------------------------------------------------------
    // IOutboxRetentionStore
    // ---------------------------------------------------------------------------

    [Fact]
    public Task DeleteProcessedAsync_WithNullTenant_ReachesNullTenantRows()
        => Task.Run(async () =>
        {
            // Defect #3 again, and the reason the outbox grew without bound in single-tenant
            // deployments: every row had a null tenant, so nothing was ever deleted.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(
                BuildOutboxMessage(OutboxMessageStatus.Published, tenantId: null, processedAtUtc: Now.AddDays(-30)),
                BuildOutboxMessage(OutboxMessageStatus.Published, tenantId: "tenant-a", processedAtUtc: Now.AddDays(-30)),
                BuildOutboxMessage(OutboxMessageStatus.Published, tenantId: null, processedAtUtc: Now.AddDays(-1)),
                BuildOutboxMessage(tenantId: null));
            await ctx.SaveChangesAsync();

            var deleted = await Store(ctx).DeleteProcessedAsync(Now.AddDays(-7));

            deleted.ShouldBe(2, "both old published rows, whatever their tenant");

            await using var probe = SecondContext(conn);
            (await probe.OutboxMessages.CountAsync()).ShouldBe(2);
        });

    [Fact]
    public Task DeleteProcessedAsync_NeverDeletesUnpublishedRows()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.AddRange(
                BuildOutboxMessage(processedAtUtc: Now.AddDays(-30)),
                BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, processedAtUtc: Now.AddDays(-30)));
            await ctx.SaveChangesAsync();

            (await Store(ctx).DeleteProcessedAsync(Now.AddDays(-7))).ShouldBe(0);
        });

    /// <summary>
    /// <c>OutboxProcessorOptions.MaxErrorMessageLength</c> documents itself as fitting the
    /// <c>ErrorMessage</c> column, but the two live in different packages with nothing linking
    /// them: the default is in Core, the column width in the EF Core configuration. Either can
    /// be changed alone, and truncation past the column would surface only as a failed
    /// settlement write in production — after a dispatch failure, which is already the worst
    /// moment to lose the batch.
    /// </summary>
    [Fact]
    public void DefaultErrorMessageLength_FitsTheMappedColumn()
    {
        var (conn, ctx) = CreateIsolatedDb();
        using var _ = conn;
        using var __ = ctx;

        var columnLength = ctx.Model
            .FindEntityType(typeof(OutboxMessage))!
            .FindProperty(nameof(OutboxMessage.ErrorMessage))!
            .GetMaxLength();

        columnLength.ShouldNotBeNull("an unbounded ErrorMessage column makes truncation pointless");
        new OutboxProcessorOptions().MaxErrorMessageLength.ShouldBeLessThanOrEqualTo(
            columnLength!.Value,
            "the truncation ceiling must fit the column it is truncating for");
        new InboxProcessorOptions().MaxErrorMessageLength.ShouldBeLessThanOrEqualTo(
            ctx.Model
                .FindEntityType(typeof(InboxMessage))!
                .FindProperty(nameof(InboxMessage.ErrorMessage))!
                .GetMaxLength()!.Value,
            "the inbox carries the same coupling");
    }

    // ---------------------------------------------------------------------------
    // Message kind, contract name, and the replay natural key
    // ---------------------------------------------------------------------------

    private static Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty Property(
        TestMessagingDbContext ctx, string name)
        => ctx.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(name)!;

    /// <summary>
    /// The column exists to be queried — session 023 calls it "routing, queryable in SQL". An int
    /// discriminator is unreadable in psql or the Supabase dashboard, which would defeat the only
    /// reason the column is there rather than inferred from the payload's CLR type.
    /// </summary>
    [Fact]
    public void MessageKind_IsMappedAsARequiredConstrainedString()
    {
        var (conn, ctx) = CreateIsolatedDb();
        using var _ = conn;
        using var __ = ctx;

        var kind = Property(ctx, nameof(OutboxMessage.MessageKind));

        kind.GetProviderClrType().ShouldBe(
            typeof(string), "an int discriminator is unreadable in a SQL console");
        kind.GetMaxLength().ShouldBe(
            32, "the width every other status column in this module uses");
        kind.IsNullable.ShouldBeFalse("every row has a nature");
    }

    /// <summary>
    /// Both are meaningful only for a contract row, so both are optional — and the pairing with
    /// <c>MessageKind</c> is deliberately not enforced here: the entity has no constructor to
    /// enforce it in, and a guard in a setter would throw part-way through materialization.
    /// </summary>
    [Fact]
    public void ContractNameAndSourceMessageId_AreOptional()
    {
        var (conn, ctx) = CreateIsolatedDb();
        using var _ = conn;
        using var __ = ctx;

        var contractName = Property(ctx, nameof(OutboxMessage.ContractName));
        contractName.IsNullable.ShouldBeTrue("a notification has no wire identity");
        contractName.GetMaxLength().ShouldBe(
            256, "must match IntegrationEventMessage.ContractName — the same notion");

        var source = Property(ctx, nameof(OutboxMessage.SourceMessageId));
        source.IsNullable.ShouldBeTrue("a row not produced by a dispatch has no source");
        source.GetValueConverter().ShouldNotBeNull("MessageId? is not a storable type on its own");
    }

    /// <summary>
    /// The index name is pinned because a consumer hand-writes their own DDL against it — the EF
    /// configuration is the schema's only definition, so the name is part of the contract.
    /// </summary>
    [Fact]
    public void TheReplayNaturalKey_IsAUniqueIndexOverSourceThenContract()
    {
        var (conn, ctx) = CreateIsolatedDb();
        using var _ = conn;
        using var __ = ctx;

        var index = ctx.Model
            .FindEntityType(typeof(OutboxMessage))!
            .GetIndexes()
            .SingleOrDefault(i =>
                i.GetDatabaseName() == "UX_OutboxMessages_Source_ContractName");

        index.ShouldNotBeNull("the replay natural key is the whole point of the two columns");
        index.IsUnique.ShouldBeTrue("without uniqueness a replay writes a duplicate silently");
        index.Properties.Select(p => p.Name).ShouldBe(
            [nameof(OutboxMessage.SourceMessageId), nameof(OutboxMessage.ContractName)]);
    }

    /// <summary>
    /// A converter that maps but does not round-trip passes every metadata assertion above.
    /// </summary>
    [Fact]
    public Task BothKinds_RoundTripThroughTheDatabase()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var source = MessageId.New();

            ctx.OutboxMessages.Add(BuildOutboxMessage());
            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1",
                sourceMessageId: source));
            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            var rows = await probe.OutboxMessages.AsNoTracking()
                .OrderBy(m => m.MessageKind)
                .ToListAsync();

            var notification = rows.Single(m => m.MessageKind == MessageKind.Notification);
            notification.ContractName.ShouldBeNull();
            notification.SourceMessageId.ShouldBeNull();

            var contract = rows.Single(m => m.MessageKind == MessageKind.Contract);
            contract.ContractName.ShouldBe("saasbtp.safety.constat-recorded.v1");
            contract.SourceMessageId.ShouldBe(source);
        });

    /// <summary>
    /// Every notification row carries (null, null). If nulls ever compared equal in this index the
    /// SECOND notification the system ever wrote would be rejected — which is precisely why SQL
    /// Server is declared unsupported for this constraint. This is the assertion that fails the day
    /// someone "improves" the index with NULLS NOT DISTINCT.
    /// </summary>
    [Fact]
    public Task Many_notification_rows_with_no_contract_key_coexist()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            for (var i = 0; i < 5; i++)
            {
                ctx.OutboxMessages.Add(BuildOutboxMessage());
            }

            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            (await probe.OutboxMessages.CountAsync()).ShouldBe(5);
        });

    /// <summary>The natural key doing its job: a replayed publish collides instead of duplicating.</summary>
    [Fact]
    public Task A_second_row_with_the_same_source_and_contract_is_rejected()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var source = MessageId.New();

            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1",
                sourceMessageId: source));
            await ctx.SaveChangesAsync();

            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1",
                sourceMessageId: source));

            await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        });

    /// <summary>
    /// The key is (source, contract), not contract alone: one contract published from many
    /// different source rows is the nominal case, not a duplicate.
    /// </summary>
    [Fact]
    public Task The_same_contract_from_two_different_sources_is_accepted()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1",
                sourceMessageId: MessageId.New()));
            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1",
                sourceMessageId: MessageId.New()));

            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            (await probe.OutboxMessages.CountAsync()).ShouldBe(2);
        });

    /// <summary>
    /// The scope of the guarantee, pinned so it is not later read as unconditional. A contract
    /// staged outside a dispatch has no source row, the tuple contains a null, and nulls are
    /// distinct — so it does not deduplicate. That is intended: the key guards the REPLAY path,
    /// where a source row always exists.
    /// </summary>
    [Fact]
    public Task A_contract_row_with_no_source_does_not_deduplicate()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1"));
            ctx.OutboxMessages.Add(BuildOutboxMessage(
                messageKind: MessageKind.Contract,
                contractName: "saasbtp.safety.constat-recorded.v1"));

            await ctx.SaveChangesAsync();

            await using var probe = SecondContext(conn);
            (await probe.OutboxMessages.CountAsync()).ShouldBe(2);
        });
}
