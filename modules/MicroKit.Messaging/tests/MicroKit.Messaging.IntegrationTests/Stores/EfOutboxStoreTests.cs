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
        Guid? claimToken = null)
    {
        return new OutboxMessage
        {
            Id = MessageId.New(),
            TenantId = tenantId,
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
}
