namespace MicroKit.Messaging.IntegrationTests.Stores;

public sealed class EfOutboxStoreTests
{
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

    private static OutboxMessage BuildOutboxMessage(
        OutboxMessageStatus status = OutboxMessageStatus.Pending,
        DateTimeOffset? lockedUntilUtc = null,
        DateTimeOffset? nextRetryAtUtc = null,
        bool deadLettered = false,
        string tenantId = "tenant-a")
    {
        return new OutboxMessage
        {
            Id = MessageId.New(),
            TenantId = tenantId,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = status,
            OccurredOnUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = CorrelationId.New(),
            LockedUntilUtc = lockedUntilUtc,
            NextRetryAtUtc = nextRetryAtUtc,
            DeadLettered = deadLettered
        };
    }

    [Fact]
    public Task AddAsync_StagesMessage_NotPersistedUntilSaveChanges()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var message = BuildOutboxMessage();

            await store.AddAsync(message);

            // Row staged but not committed yet — count from a new context to verify isolation
            await using var ctx2 = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
            var countBefore = await ctx2.OutboxMessages.CountAsync();
            countBefore.ShouldBe(0);

            await ctx.SaveChangesAsync();
            var countAfter = await ctx2.OutboxMessages.CountAsync();
            countAfter.ShouldBe(1);
        });

    [Fact]
    public Task GetPendingAsync_ReturnsOnlyEligibleMessages()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var pending = BuildOutboxMessage(OutboxMessageStatus.Pending);
            var published = BuildOutboxMessage(OutboxMessageStatus.Published);
            ctx.OutboxMessages.AddRange(pending, published);
            await ctx.SaveChangesAsync();

            var result = await store.GetPendingAsync(10);

            result.Count.ShouldBe(1);
            result[0].Id.ShouldBe(pending.Id);
        });

    [Fact]
    public Task GetPendingAsync_IncludesExpiredLease()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var stale = BuildOutboxMessage(
                OutboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
            ctx.OutboxMessages.Add(stale);
            await ctx.SaveChangesAsync();

            var result = await store.GetPendingAsync(10);

            result.Count.ShouldBe(1);
            result[0].Id.ShouldBe(stale.Id);
        });

    [Fact]
    public Task GetPendingAsync_ExcludesMessageWithFutureNextRetryAtUtc()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var backingOff = BuildOutboxMessage(
                OutboxMessageStatus.Pending,
                nextRetryAtUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.OutboxMessages.Add(backingOff);
            await ctx.SaveChangesAsync();

            var result = await store.GetPendingAsync(10);

            result.ShouldBeEmpty();
        });

    [Fact]
    public Task AcquireLeaseAsync_WhenPending_ReturnsTrue()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var message = BuildOutboxMessage(OutboxMessageStatus.Pending);
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var acquired = await store.AcquireLeaseAsync(message.Id, DateTimeOffset.UtcNow.AddMinutes(5));

            acquired.ShouldBeTrue();
        });

    [Fact]
    public Task AcquireLeaseAsync_WhenAlreadyLeased_ReturnsFalse()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var message = BuildOutboxMessage(
                OutboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var acquired = await store.AcquireLeaseAsync(message.Id, DateTimeOffset.UtcNow.AddMinutes(5));

            acquired.ShouldBeFalse();
        });

    [Fact]
    public Task AcquireLeaseAsync_WhenExpiredLease_ReturnsTrue()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            // Stale lease: status=Processing but lock expired — must be reacquirable (BLOCK-1 fix)
            var stale = BuildOutboxMessage(
                OutboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
            ctx.OutboxMessages.Add(stale);
            await ctx.SaveChangesAsync();

            var acquired = await store.AcquireLeaseAsync(stale.Id, DateTimeOffset.UtcNow.AddMinutes(5));

            acquired.ShouldBeTrue();
        });

    [Fact]
    public Task AcquireLeaseAsync_TwoProcessors_EachMessageProcessedOnce()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            // Two store instances on the same connection simulate two concurrent processors
            var store1 = new EfOutboxStore<TestMessagingDbContext>(ctx);
            await using var ctx2 = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
            var store2 = new EfOutboxStore<TestMessagingDbContext>(ctx2);

            var message = BuildOutboxMessage(OutboxMessageStatus.Pending);
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var lockExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
            var result1 = await store1.AcquireLeaseAsync(message.Id, lockExpiry);
            var result2 = await store2.AcquireLeaseAsync(message.Id, lockExpiry);

            // Exactly one acquirer wins — the second races on a now-Processing row
            (result1 ^ result2).ShouldBeTrue("exactly one processor must win the lease");
        });

    [Fact]
    public Task MarkPublishedAsync_SetsTerminalState()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var message = BuildOutboxMessage(OutboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var result = await store.MarkPublishedAsync(message.Id);

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.Id == message.Id);
            row.Status.ShouldBe(OutboxMessageStatus.Published);
            row.ProcessedAtUtc.ShouldNotBeNull();
            row.LockedUntilUtc.ShouldBeNull();
        });

    [Fact]
    public Task MarkFailedAsync_ResetsStatusToPending_SetsBackOff()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var message = BuildOutboxMessage(OutboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var before = DateTimeOffset.UtcNow;
            var result = await store.MarkFailedAsync(message.Id, "transient error", retryCount: 1);

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.Id == message.Id);
            row.Status.ShouldBe(OutboxMessageStatus.Pending);
            row.RetryCount.ShouldBe(1);
            row.ErrorMessage.ShouldBe("transient error");
            row.LockedUntilUtc.ShouldBeNull();
            // 2^1 = 2 seconds back-off
            row.NextRetryAtUtc.ShouldNotBeNull();
            row.NextRetryAtUtc!.Value.ShouldBeGreaterThan(before.AddSeconds(1));
        });

    [Fact]
    public Task DeadLetterAsync_SetsFailedTerminalState()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var message = BuildOutboxMessage(OutboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.OutboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var result = await store.DeadLetterAsync(message.Id, "max retries exceeded");

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.Id == message.Id);
            row.Status.ShouldBe(OutboxMessageStatus.Failed);
            row.DeadLettered.ShouldBeTrue();
            row.ProcessedAtUtc.ShouldNotBeNull();
            row.LockedUntilUtc.ShouldBeNull();
            row.ErrorMessage.ShouldBe("max retries exceeded");
        });

    [Fact]
    public Task DeleteProcessedAsync_DeletesEligibleRows_ReturnsCount()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var old = BuildOutboxMessage(OutboxMessageStatus.Published, tenantId: "tenant-a");
            old.ProcessedAtUtc = DateTimeOffset.UtcNow.AddDays(-8);
            var recent = BuildOutboxMessage(OutboxMessageStatus.Published, tenantId: "tenant-a");
            recent.ProcessedAtUtc = DateTimeOffset.UtcNow;
            ctx.OutboxMessages.AddRange(old, recent);
            await ctx.SaveChangesAsync();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            var deleted = await store.DeleteProcessedAsync(cutoff, "tenant-a");

            deleted.ShouldBe(1);
            var remaining = await ctx.OutboxMessages.AsNoTracking().CountAsync();
            remaining.ShouldBe(1);
        });

    [Fact]
    public Task GetDeadLetteredAsync_ReturnsDeadLetteredForTenant()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var dl = BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, tenantId: "tenant-a");
            var other = BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true, tenantId: "tenant-b");
            ctx.OutboxMessages.AddRange(dl, other);
            await ctx.SaveChangesAsync();

            var result = await store.GetDeadLetteredAsync(10, "tenant-a");

            result.Count.ShouldBe(1);
            result[0].TenantId.ShouldBe("tenant-a");
        });

    [Fact]
    public Task RequeueAsync_ResetsDeadLetteredMessageToPending()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfOutboxStore<TestMessagingDbContext>(ctx);
            var dl = BuildOutboxMessage(OutboxMessageStatus.Failed, deadLettered: true);
            dl.RetryCount = 10;
            dl.ErrorMessage = "too many failures";
            ctx.OutboxMessages.Add(dl);
            await ctx.SaveChangesAsync();

            var result = await store.RequeueAsync(dl.Id);

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.Id == dl.Id);
            row.Status.ShouldBe(OutboxMessageStatus.Pending);
            row.DeadLettered.ShouldBeFalse();
            row.RetryCount.ShouldBe(0);
            row.NextRetryAtUtc.ShouldBeNull();
            row.ErrorMessage.ShouldBeNull();
            row.LockedUntilUtc.ShouldBeNull();
        });
}
