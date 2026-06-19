namespace MicroKit.Messaging.IntegrationTests.Stores;

public sealed class EfInboxStoreTests
{
    private static (SqliteConnection conn, TestMessagingDbContext ctx) CreateIsolatedDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var ctx = new TestMessagingDbContext(
            new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return (conn, ctx);
    }

    private static InboxMessage BuildInboxMessage(
        MessageId? messageId = null,
        string consumerType = "MicroKit.Test.TestHandler, MicroKit.Test",
        InboxMessageStatus status = InboxMessageStatus.Received,
        DateTimeOffset? lockedUntilUtc = null,
        DateTimeOffset? nextRetryAtUtc = null,
        string tenantId = "tenant-a")
    {
        return new InboxMessage
        {
            MessageId = messageId ?? MessageId.New(),
            ConsumerType = consumerType,
            TenantId = tenantId,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = status,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            LockedUntilUtc = lockedUntilUtc,
            NextRetryAtUtc = nextRetryAtUtc
        };
    }

    [Fact]
    public Task ExistsAsync_WhenNotPresent_ReturnsFalse()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);

            var exists = await store.ExistsAsync(MessageId.New(), "SomeConsumer");

            exists.ShouldBeFalse();
        });

    [Fact]
    public Task ExistsAsync_WhenPresent_ReturnsTrue()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage();
            await store.AddAsync(message);

            var exists = await store.ExistsAsync(message.MessageId, message.ConsumerType);

            exists.ShouldBeTrue();
        });

    [Fact]
    public Task AddAsync_PersistsRow()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage();

            await store.AddAsync(message);

            var count = await ctx.InboxMessages.AsNoTracking().CountAsync();
            count.ShouldBe(1);
        });

    [Fact]
    public Task AddAsync_WhenDuplicate_ThrowsDbUpdateException()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage();
            await store.AddAsync(message);

            await using var ctx2 = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
            var store2 = new EfInboxStore<TestMessagingDbContext>(ctx2);
            var duplicate = BuildInboxMessage(
                messageId: message.MessageId,
                consumerType: message.ConsumerType);

            await Should.ThrowAsync<DbUpdateException>(
                async () => await store2.AddAsync(duplicate));
        });

    [Fact]
    public Task AddAsync_WithSameMessageIdDifferentConsumer_StoresTwoRows()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var sharedMessageId = MessageId.New();
            var row1 = BuildInboxMessage(messageId: sharedMessageId,
                consumerType: "MicroKit.Test.HandlerA, MicroKit.Test");
            var row2 = BuildInboxMessage(messageId: sharedMessageId,
                consumerType: "MicroKit.Test.HandlerB, MicroKit.Test");

            await store.AddAsync(row1);
            await using var ctx2 = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
            var store2 = new EfInboxStore<TestMessagingDbContext>(ctx2);
            await store2.AddAsync(row2);

            var count = await ctx.InboxMessages.AsNoTracking().CountAsync();
            count.ShouldBe(2);
        });

    [Fact]
    public Task GetPendingAsync_ReturnsOnlyEligibleMessages()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var pending = BuildInboxMessage(status: InboxMessageStatus.Received);
            var processed = BuildInboxMessage(consumerType: "HandlerB",
                status: InboxMessageStatus.Processed);
            ctx.InboxMessages.AddRange(pending, processed);
            await ctx.SaveChangesAsync();

            var result = await store.GetPendingAsync(10);

            result.Count.ShouldBe(1);
            result[0].MessageId.ShouldBe(pending.MessageId);
        });

    [Fact]
    public Task GetPendingAsync_IncludesExpiredLease()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var stale = BuildInboxMessage(
                status: InboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
            ctx.InboxMessages.Add(stale);
            await ctx.SaveChangesAsync();

            var result = await store.GetPendingAsync(10);

            result.Count.ShouldBe(1);
        });

    [Fact]
    public Task MarkProcessingAsync_SetsLease()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage();
            ctx.InboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var lockUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            await store.MarkProcessingAsync(message.MessageId, message.ConsumerType, lockUntil);

            var row = await ctx.InboxMessages.AsNoTracking()
                .SingleAsync(m => m.MessageId == message.MessageId
                    && m.ConsumerType == message.ConsumerType);
            row.Status.ShouldBe(InboxMessageStatus.Processing);
            row.LockedUntilUtc.ShouldNotBeNull();
        });

    [Fact]
    public Task MarkProcessedAsync_SetsTerminalState()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage(
                status: InboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.InboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var result = await store.MarkProcessedAsync(message.MessageId, message.ConsumerType);

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.InboxMessages.AsNoTracking()
                .SingleAsync(m => m.MessageId == message.MessageId
                    && m.ConsumerType == message.ConsumerType);
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ProcessedAtUtc.ShouldNotBeNull();
            row.LockedUntilUtc.ShouldBeNull();
        });

    [Fact]
    public Task MarkFailedAsync_ResetsToReceived_SetsBackOff()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage(
                status: InboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.InboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var before = DateTimeOffset.UtcNow;
            var result = await store.MarkFailedAsync(
                message.MessageId, message.ConsumerType, "handler threw", retryCount: 2);

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.InboxMessages.AsNoTracking()
                .SingleAsync(m => m.MessageId == message.MessageId
                    && m.ConsumerType == message.ConsumerType);
            row.Status.ShouldBe(InboxMessageStatus.Received);
            row.RetryCount.ShouldBe(2);
            row.ErrorMessage.ShouldBe("handler threw");
            row.LockedUntilUtc.ShouldBeNull();
            // 2^2 = 4 seconds back-off
            row.NextRetryAtUtc.ShouldNotBeNull();
            row.NextRetryAtUtc!.Value.ShouldBeGreaterThan(before.AddSeconds(3));
        });

    [Fact]
    public Task DeadLetterAsync_SetsFailedTerminalState()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = new EfInboxStore<TestMessagingDbContext>(ctx);
            var message = BuildInboxMessage(
                status: InboxMessageStatus.Processing,
                lockedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5));
            ctx.InboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var result = await store.DeadLetterAsync(
                message.MessageId, message.ConsumerType, "max retries exceeded");

            result.IsSuccess.ShouldBeTrue();
            var row = await ctx.InboxMessages.AsNoTracking()
                .SingleAsync(m => m.MessageId == message.MessageId
                    && m.ConsumerType == message.ConsumerType);
            row.Status.ShouldBe(InboxMessageStatus.Failed);
            row.DeadLettered.ShouldBeTrue();
            row.ProcessedAtUtc.ShouldNotBeNull();
            row.LockedUntilUtc.ShouldBeNull();
            row.ErrorMessage.ShouldBe("max retries exceeded");
        });
}
