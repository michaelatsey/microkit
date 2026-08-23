using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// The inbox claim and settlement contract under SQLite. The claim carries no provider-specific
/// SQL, so the production code path is exactly what runs here.
/// </summary>
/// <remarks>
/// What SQLite cannot prove lives in <c>PostgreSql/InboxClaimConcurrencyTests</c> and
/// <c>PostgreSql/InboxLeaseExpiryTests</c>: genuine server-side contention, and the concurrency
/// token rejecting a write from inside a handler's own transaction.
/// </remarks>
public sealed class EfInboxStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static readonly string[] CrossProductConsumers = ["HandlerA", "HandlerB", "HandlerC"];

    // Isolated SQLite connection per test (ADR-MSG testing rule).
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

    private static EfInboxStore<TestMessagingDbContext> Store(
        TestMessagingDbContext ctx, DateTimeOffset? now = null)
        => new(ctx, new FakeTimeProvider(now ?? Now));

    private static InboxMessage BuildInboxMessage(
        MessageId? messageId = null,
        string consumerType = "MicroKit.Test.TestHandler, MicroKit.Test",
        InboxMessageStatus status = InboxMessageStatus.Received,
        DateTimeOffset? lockedUntilUtc = null,
        DateTimeOffset? nextRetryAtUtc = null,
        DateTimeOffset? receivedAtUtc = null,
        DateTimeOffset? processedAtUtc = null,
        bool deadLettered = false,
        Guid? claimToken = null,
        string? tenantId = "tenant-a")
    {
        return new InboxMessage
        {
            RowId = Guid.NewGuid(),
            MessageId = messageId ?? MessageId.New(),
            ConsumerType = consumerType,
            TenantId = tenantId,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = status,
            ReceivedAtUtc = receivedAtUtc ?? Now,
            ProcessedAtUtc = processedAtUtc,
            LockedUntilUtc = lockedUntilUtc,
            NextRetryAtUtc = nextRetryAtUtc,
            DeadLettered = deadLettered,
            ClaimToken = claimToken,
        };
    }

    // ---------------------------------------------------------------------------
    // Ingestion
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ExistsAsync_WhenNotPresent_ReturnsFalse()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            (await Store(ctx).ExistsAsync(MessageId.New(), "SomeConsumer")).ShouldBeFalse();
        });

    [Fact]
    public Task ExistsAsync_WhenPresent_ReturnsTrue()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = Store(ctx);
            var message = BuildInboxMessage();
            await store.AddAsync(message);

            (await store.ExistsAsync(message.MessageId, message.ConsumerType)).ShouldBeTrue();
        });

    [Fact]
    public Task AddAsync_WithSameMessageIdDifferentConsumer_StoresTwoRows()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var sharedMessageId = MessageId.New();
            var store = Store(ctx);

            (await store.AddAsync(BuildInboxMessage(sharedMessageId, "HandlerA")))
                .ShouldBe(InboxWriteResult.Added);
            (await store.AddAsync(BuildInboxMessage(sharedMessageId, "HandlerB")))
                .ShouldBe(InboxWriteResult.Added);

            (await ctx.InboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);
        });

    // ---------------------------------------------------------------------------
    // Claim
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ClaimBatchAsync_StampsStatusLeaseAndToken()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var message = BuildInboxMessage();
            ctx.InboxMessages.Add(message);
            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(10, Lease);

            claim.Count.ShouldBe(1);
            claim.Token.ShouldNotBe(Guid.Empty);

            var row = await SecondContext(conn).InboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processing);
            row.ClaimToken.ShouldBe(claim.Token);
            row.LockedUntilUtc.ShouldBe(Now.Add(Lease));
        });

    [Fact]
    public Task ClaimBatchAsync_WhenNothingProcessable_ReturnsEmpty()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.AddRange(
                BuildInboxMessage(status: InboxMessageStatus.Processed, consumerType: "A"),
                BuildInboxMessage(deadLettered: true, status: InboxMessageStatus.Failed, consumerType: "B"),
                BuildInboxMessage(consumerType: "C", nextRetryAtUtc: Now.AddMinutes(5)));
            await ctx.SaveChangesAsync();

            (await Store(ctx).ClaimBatchAsync(10, Lease)).Count.ShouldBe(0);
        });

    [Fact]
    public Task ClaimBatchAsync_RecoversAnExpiredLease()
        => Task.Run(async () =>
        {
            // Without this arm a crashed processor's rows are stuck until someone intervenes.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.Add(BuildInboxMessage(
                status: InboxMessageStatus.Processing,
                lockedUntilUtc: Now.AddMinutes(-1),
                claimToken: Guid.NewGuid()));
            await ctx.SaveChangesAsync();

            (await Store(ctx).ClaimBatchAsync(10, Lease)).Count.ShouldBe(1);
        });

    [Fact]
    public Task ClaimBatchAsync_DoesNotStealALiveLease()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.Add(BuildInboxMessage(
                status: InboxMessageStatus.Processing,
                lockedUntilUtc: Now.AddMinutes(4),
                claimToken: Guid.NewGuid()));
            await ctx.SaveChangesAsync();

            (await Store(ctx).ClaimBatchAsync(10, Lease)).Count.ShouldBe(0);
        });

    [Fact]
    public Task ClaimBatchAsync_NeverExceedsBatchSize()
        => Task.Run(async () =>
        {
            // The cross-product bug: filtering on two Contains over the compound key matched
            // every combination, so 6 rows spanning 3 consumers could claim up to 18.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            foreach (var messageId in Enumerable.Range(0, 6).Select(_ => MessageId.New()))
            {
                foreach (var consumer in CrossProductConsumers)
                {
                    ctx.InboxMessages.Add(BuildInboxMessage(messageId, consumer));
                }
            }

            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(batchSize: 4, Lease);

            claim.Count.ShouldBe(4);
            (await SecondContext(conn).InboxMessages.AsNoTracking()
                .CountAsync(m => m.ClaimToken != null)).ShouldBe(4);
        });

    [Fact]
    public Task ClaimBatchAsync_TwoProcessorsNeverWinTheSameRow()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.Add(BuildInboxMessage());
            await ctx.SaveChangesAsync();

            var first = await Store(ctx).ClaimBatchAsync(10, Lease);
            await using var ctx2 = SecondContext(conn);
            var second = await Store(ctx2).ClaimBatchAsync(10, Lease);

            first.Count.ShouldBe(1);
            second.Count.ShouldBe(0, "the eligibility predicate is replayed inside the UPDATE");
        });

    [Fact]
    public Task ClaimBatchAsync_OrdersByReceivedAt()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var oldest = BuildInboxMessage(consumerType: "A", receivedAtUtc: Now.AddMinutes(-30));
            ctx.InboxMessages.AddRange(
                BuildInboxMessage(consumerType: "B", receivedAtUtc: Now.AddMinutes(-1)),
                oldest,
                BuildInboxMessage(consumerType: "C", receivedAtUtc: Now.AddMinutes(-10)));
            await ctx.SaveChangesAsync();

            var claim = await Store(ctx).ClaimBatchAsync(batchSize: 1, Lease);

            claim.Messages[0].MessageId.ShouldBe(oldest.MessageId);
        });

    // ---------------------------------------------------------------------------
    // Settlement — token guard
    // ---------------------------------------------------------------------------

    [Fact]
    public Task ApplyOutcomesAsync_WithTheOwningToken_WritesEveryDisposition()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.AddRange(
                BuildInboxMessage(consumerType: "A"),
                BuildInboxMessage(consumerType: "B"),
                BuildInboxMessage(consumerType: "C"),
                BuildInboxMessage(consumerType: "D"));
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var keys = claim.Messages
                .Select(m => new InboxMessageKey(m.MessageId, m.ConsumerType))
                .ToList();

            var written = await store.ApplyOutcomesAsync(claim.Token,
            [
                InboxOutcome.Processed(keys[0]),
                InboxOutcome.Retry(keys[1], 2, Now.AddSeconds(4), "transient"),
                InboxOutcome.DeadLetter(keys[2], 5, "poison"),
                InboxOutcome.Released(keys[3]),
            ]);

            written.ShouldBe(4);

            var rows = await SecondContext(conn).InboxMessages.AsNoTracking()
                .ToDictionaryAsync(m => m.ConsumerType);

            var processed = rows[keys[0].ConsumerType];
            processed.Status.ShouldBe(InboxMessageStatus.Processed);
            processed.ProcessedAtUtc.ShouldBe(Now);
            processed.ClaimToken.ShouldBeNull();

            var retried = rows[keys[1].ConsumerType];
            retried.Status.ShouldBe(InboxMessageStatus.Received);
            retried.RetryCount.ShouldBe(2);
            retried.NextRetryAtUtc.ShouldBe(Now.AddSeconds(4));
            retried.ErrorMessage.ShouldBe("transient");
            retried.ClaimToken.ShouldBeNull();

            var dead = rows[keys[2].ConsumerType];
            dead.Status.ShouldBe(InboxMessageStatus.Failed);
            dead.DeadLettered.ShouldBeTrue();
            dead.RetryCount.ShouldBe(5);
            dead.ClaimToken.ShouldBeNull();

            var released = rows[keys[3].ConsumerType];
            released.Status.ShouldBe(InboxMessageStatus.Received);
            released.RetryCount.ShouldBe(0, "a release consumes no retry budget");
            released.NextRetryAtUtc.ShouldBeNull();
            released.ClaimToken.ShouldBeNull();
        });

    [Fact]
    public Task ApplyOutcomesAsync_WithAStaleToken_WritesNothing()
        => Task.Run(async () =>
        {
            // The lost update the token exists to prevent: a processor whose lease expired must
            // not overwrite the processor that legitimately re-claimed its row.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.Add(BuildInboxMessage());
            await ctx.SaveChangesAsync();

            var staleStore = Store(ctx);
            var staleClaim = await staleStore.ClaimBatchAsync(10, Lease);

            await using var ctx2 = SecondContext(conn);
            var freshStore = Store(ctx2, Now.Add(Lease).AddMinutes(1));
            var freshClaim = await freshStore.ClaimBatchAsync(10, Lease);

            freshClaim.Count.ShouldBe(1, "an expired lease must be re-claimable");
            freshClaim.Token.ShouldNotBe(staleClaim.Token);

            var key = new InboxMessageKey(
                staleClaim.Messages[0].MessageId, staleClaim.Messages[0].ConsumerType);
            var written = await staleStore.ApplyOutcomesAsync(
                staleClaim.Token, [InboxOutcome.Processed(key)]);

            written.ShouldBe(0, "a lost lease must yield zero rows, never a silent overwrite");

            var row = await SecondContext(conn).InboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processing);
            row.ClaimToken.ShouldBe(freshClaim.Token);
        });

    [Fact]
    public Task ApplyOutcomesAsync_DoesNotTouchASiblingConsumersRow()
        => Task.Run(async () =>
        {
            // Settling by RowId rather than by the compound key is what prevents this.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var sharedMessageId = MessageId.New();
            ctx.InboxMessages.AddRange(
                BuildInboxMessage(sharedMessageId, "HandlerA"),
                BuildInboxMessage(sharedMessageId, "HandlerB"));
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);

            await store.ApplyOutcomesAsync(claim.Token,
                [InboxOutcome.Processed(new InboxMessageKey(sharedMessageId, "HandlerA"))]);

            var rows = await SecondContext(conn).InboxMessages.AsNoTracking()
                .ToDictionaryAsync(m => m.ConsumerType);

            rows["HandlerA"].Status.ShouldBe(InboxMessageStatus.Processed);
            rows["HandlerB"].Status.ShouldBe(InboxMessageStatus.Processing, "still claimed, untouched");
        });

    // ---------------------------------------------------------------------------
    // Staged settlement
    // ---------------------------------------------------------------------------

    [Fact]
    public Task StageProcessedAsync_StagesWithoutCommitting()
        => Task.Run(async () =>
        {
            // ExecuteUpdate would commit on its own, reopening the gap this method exists to
            // close. The mark must ride the handler's own SaveChanges.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.Add(BuildInboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var key = new InboxMessageKey(
                claim.Messages[0].MessageId, claim.Messages[0].ConsumerType);

            (await store.StageProcessedAsync(key, claim.Token)).ShouldBeTrue();
            store.IsMarkUncommitted(key).ShouldBeTrue();

            var beforeSave = await SecondContext(conn).InboxMessages.AsNoTracking().SingleAsync();
            beforeSave.Status.ShouldBe(InboxMessageStatus.Processing, "nothing committed yet");

            await ctx.SaveChangesAsync(); // the handler's unit of work

            store.IsMarkUncommitted(key).ShouldBeFalse();
            var afterSave = await SecondContext(conn).InboxMessages.AsNoTracking().SingleAsync();
            afterSave.Status.ShouldBe(InboxMessageStatus.Processed);
            afterSave.ClaimToken.ShouldBeNull("a committed success clears the token");
        });

    [Fact]
    public Task StageProcessedAsync_WithAStaleToken_ReturnsFalse()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.Add(BuildInboxMessage());
            await ctx.SaveChangesAsync();

            var store = Store(ctx);
            var claim = await store.ClaimBatchAsync(10, Lease);
            var key = new InboxMessageKey(
                claim.Messages[0].MessageId, claim.Messages[0].ConsumerType);

            (await store.StageProcessedAsync(key, Guid.NewGuid())).ShouldBeFalse();
            store.IsMarkUncommitted(key).ShouldBeFalse("nothing was staged");
        });

    [Fact]
    public Task IsLeaseLost_DiscriminatesStructurally()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var store = Store(ctx);

            store.IsLeaseLost(new InvalidOperationException("domain conflict")).ShouldBeFalse();
            store.IsLeaseLost(new DbUpdateConcurrencyException("no entries")).ShouldBeFalse(
                "a domain concurrency conflict is legitimately transient and must keep its retry");
        });

    // ---------------------------------------------------------------------------
    // Admin and retention — the single-tenant null case the old signature never matched
    // ---------------------------------------------------------------------------

    [Fact]
    public Task GetDeadLetteredAsync_WithNullTenant_MatchesRowsThatHaveNoTenant()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.AddRange(
                BuildInboxMessage(consumerType: "A", deadLettered: true,
                    status: InboxMessageStatus.Failed, tenantId: null),
                BuildInboxMessage(consumerType: "B", deadLettered: true,
                    status: InboxMessageStatus.Failed, tenantId: "tenant-a"),
                BuildInboxMessage(consumerType: "C"));
            await ctx.SaveChangesAsync();

            var all = await Store(ctx).GetDeadLetteredAsync(10);
            all.Count.ShouldBe(2, "null means every tenant, including rows with a null tenant");

            var scoped = await Store(ctx).GetDeadLetteredAsync(10, "tenant-a");
            scoped.Count.ShouldBe(1);
        });

    [Fact]
    public Task RequeueAsync_ReturnsWhetherARowWasActuallyRequeued()
        => Task.Run(async () =>
        {
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            var dead = BuildInboxMessage(
                consumerType: "A", deadLettered: true, status: InboxMessageStatus.Failed);
            var alive = BuildInboxMessage(consumerType: "B");
            ctx.InboxMessages.AddRange(dead, alive);
            await ctx.SaveChangesAsync();

            var store = Store(ctx);

            (await store.RequeueAsync(new InboxMessageKey(dead.MessageId, "A"))).ShouldBeTrue();
            (await store.RequeueAsync(new InboxMessageKey(alive.MessageId, "B"))).ShouldBeFalse(
                "zero rows means the message was not dead-lettered, which the operator must see");

            var row = await SecondContext(conn).InboxMessages.AsNoTracking()
                .SingleAsync(m => m.ConsumerType == "A");
            row.Status.ShouldBe(InboxMessageStatus.Received);
            row.DeadLettered.ShouldBeFalse();
            row.RetryCount.ShouldBe(0);
            row.NextRetryAtUtc.ShouldBeNull();
        });

    [Fact]
    public Task DeleteProcessedAsync_WithNullTenant_DeletesRowsThatHaveNoTenant()
        => Task.Run(async () =>
        {
            // The defect: a non-nullable tenantId compared against a nullable column matched
            // nothing when every row has a null tenant, so the table grew without bound.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.AddRange(
                BuildInboxMessage(consumerType: "A", status: InboxMessageStatus.Processed,
                    processedAtUtc: Now.AddDays(-40), tenantId: null),
                BuildInboxMessage(consumerType: "B", status: InboxMessageStatus.Processed,
                    processedAtUtc: Now.AddDays(-1), tenantId: null),
                BuildInboxMessage(consumerType: "C", tenantId: null));
            await ctx.SaveChangesAsync();

            var deleted = await Store(ctx).DeleteProcessedAsync(Now.AddDays(-30));

            deleted.ShouldBe(1);
            (await SecondContext(conn).InboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);
        });

    [Fact]
    public Task DeleteProcessedAsync_NeverDeletesAnUnprocessedRow()
        => Task.Run(async () =>
        {
            // Deleting a row the inbox still needs is how retention reopens reprocessing.
            var (conn, ctx) = CreateIsolatedDb();
            await using var _ = conn;
            await using var __ = ctx;

            ctx.InboxMessages.AddRange(
                BuildInboxMessage(consumerType: "A", receivedAtUtc: Now.AddDays(-400)),
                BuildInboxMessage(consumerType: "B", status: InboxMessageStatus.Failed,
                    deadLettered: true, processedAtUtc: Now.AddDays(-400)));
            await ctx.SaveChangesAsync();

            (await Store(ctx).DeleteProcessedAsync(Now)).ShouldBe(0);
            (await SecondContext(conn).InboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);
        });
}
