using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The inbox claim under genuine server-side contention.
/// </summary>
/// <remarks>
/// <para>
/// This suite exists to prove the assumption the claim rests on and that SQLite cannot test:
/// that under <c>READ COMMITTED</c> — the PostgreSQL default — a blocked <c>UPDATE</c>
/// re-evaluates its <c>WHERE</c> against the committed row version once the lock is released, so
/// the eligibility predicate replayed inside the claim rejects the loser correctly. That is what
/// makes the token claim safe without <c>FOR UPDATE SKIP LOCKED</c>.
/// </para>
/// <para>
/// The claim is an <b>optimistic</b> strategy whose only atomic point is the stamping
/// <c>UPDATE</c>. Provider-neutral is not the same as correct under every isolation
/// configuration; that has to be demonstrated, not asserted.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class InboxClaimConcurrencyTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static readonly string[] Consumers =
        ["HandlerA", "HandlerB", "HandlerC", "HandlerD", "HandlerE"];

    /// <summary>
    /// Two processors, 100 rows, batchSize 20 — including rows that share a
    /// <c>MessageId</c> across different <c>ConsumerType</c> values.
    /// </summary>
    /// <remarks>
    /// The last assertion is the one the cross-product bug would have failed: filtering the
    /// claim UPDATE with two <c>Contains</c> over the compound key matched every combination,
    /// not the candidate pairs, so 20 candidates spanning 5 consumers could claim up to 100 rows.
    /// </remarks>
    [DockerRequiredFact]
    public async Task ClaimBatchAsync_TwoProcessorsOverAFannedOutQueue_AreDisjointAndBounded()
    {
        const int MessageCount = 20;   // x 5 consumers = 100 rows
        const int ProcessorCount = 2;
        const int BatchSize = 20;

        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);
        var seeded = await SeedFannedOutAsync(schema.ConnectionString, MessageCount);
        seeded.Count.ShouldBe(100);

        var claimSizes = new System.Collections.Concurrent.ConcurrentBag<int>();
        var claimed = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        var processors = Enumerable.Range(0, ProcessorCount).Select(async _ =>
        {
            // Its own DbContext, hence its own connection: the point is genuine server-side
            // contention, not interleaved calls on one connection.
            await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
            var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

            while (true)
            {
                var claim = await store.ClaimBatchAsync(BatchSize, Lease);
                if (claim.Count == 0)
                {
                    return;
                }

                claimSizes.Add(claim.Count);

                foreach (var message in claim.Messages)
                {
                    claimed.Add(message.RowId);
                }

                // Settle as Processed so the rows leave the eligible set and the loop terminates.
                await store.ApplyOutcomesAsync(
                    claim.Token,
                    claim.Messages
                        .Select(m => InboxOutcome.Processed(
                            new InboxMessageKey(m.MessageId, m.ConsumerType)))
                        .ToList());
            }
        });

        await Task.WhenAll(processors);

        var claimedList = claimed.ToList();

        // Count == N catches OVERLAP (a duplicate would push it over).
        claimedList.Count.ShouldBe(
            seeded.Count, "zero overlap: no row may be claimed by two processors");

        // Distinct count == N catches LOSS (a dropped row would push it under).
        claimedList.ToHashSet().Count.ShouldBe(
            seeded.Count, "zero loss: every row must be claimed exactly once");

        claimedList.ToHashSet().SetEquals(seeded).ShouldBeTrue();

        // The cross-product assertion. Nothing above would have caught it: the run still
        // terminates and still settles every row.
        claimSizes.ShouldAllBe(size => size <= BatchSize);

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        (await probe.InboxMessages.CountAsync(m => m.Status == InboxMessageStatus.Processed))
            .ShouldBe(seeded.Count);
    }

    [DockerRequiredFact]
    public async Task ClaimBatchAsync_WhenManyProcessorsRaceOnOneRow_ExactlyOneWins()
    {
        const int ProcessorCount = 16;

        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);
        await SeedFannedOutAsync(schema.ConnectionString, messageCount: 1, consumerCount: 1);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var races = Enumerable.Range(0, ProcessorCount).Select(async _ =>
        {
            await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
            var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

            await start.Task;
            return await store.ClaimBatchAsync(10, Lease);
        }).ToList();

        start.SetResult();
        var claims = await Task.WhenAll(races);

        claims.Count(c => c.Count == 1).ShouldBe(1, "exactly one processor may win the row");
        claims.Count(c => c.Count == 0).ShouldBe(ProcessorCount - 1);
    }

    /// <summary>
    /// The claim filters on a list of raw <see cref="Guid"/> surrogate keys. Client-side
    /// evaluation or a broken <c>IN</c> list would silently claim the wrong rows, so this pins
    /// server-side translation rather than assuming it.
    /// </summary>
    [DockerRequiredFact]
    public async Task ClaimBatchAsync_TranslatesTheRowIdContainsFilter()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);
        await SeedFannedOutAsync(schema.ConnectionString, messageCount: 4, consumerCount: 3);

        var sql = new List<string>();
        await using var context = new TestMessagingDbContext(
            new DbContextOptionsBuilder<TestMessagingDbContext>()
                .UseNpgsql(schema.ConnectionString)
                .LogTo(line => sql.Add(line), [DbLoggerCategory.Database.Command.Name])
                .Options);

        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

        var claim = await store.ClaimBatchAsync(batchSize: 12, Lease);

        claim.Count.ShouldBe(
            12, "a client-side evaluation or a broken IN list would not claim all 12");

        // The stamping UPDATE must carry the surrogate keys into the server, not filter in memory.
        var update = sql.FirstOrDefault(line =>
            line.Contains("UPDATE \"InboxMessages\"", StringComparison.Ordinal));

        update.ShouldNotBeNull("the claim must issue a server-side UPDATE");

        // The captured SQL, rather than an assumption:
        //
        //   UPDATE "InboxMessages" AS i
        //   SET "Status" = @p, "LockedUntilUtc" = @p4, "ClaimToken" = @p5
        //   WHERE i."RowId" = ANY (@candidateRowIds)
        //     AND NOT (i."DeadLettered")
        //     AND (i."Status" = 'Received'
        //          OR (i."Status" = 'Processing' AND i."LockedUntilUtc" <= @now))
        //     AND (i."NextRetryAtUtc" IS NULL OR i."NextRetryAtUtc" <= @now)
        //
        // `= ANY (@parameter)` is the form that matters: one bound array parameter, so the plan
        // is cached whatever the batch size. An expanded IN list of literals would work and
        // silently defeat plan caching on the module's hottest write path.
        update.ShouldContain("\"RowId\" = ANY (@candidateRowIds)");

        // And the eligibility predicate is replayed inside the UPDATE — that replay is what
        // rejects a row another processor claimed between the candidate SELECT and this stamp.
        update.ShouldContain("\"DeadLettered\"");
        update.ShouldContain("\"LockedUntilUtc\" <=");

        sql.ShouldNotContain(
            line => line.Contains("could not be translated", StringComparison.Ordinal));
    }

    /// <summary>Seeds <paramref name="messageCount"/> messages fanned out across consumers.</summary>
    private static async Task<HashSet<Guid>> SeedFannedOutAsync(
        string connectionString, int messageCount, int consumerCount = 5)
    {
        await using var context = InboxTestDatabase.NewContext(connectionString);

        var rowIds = new HashSet<Guid>();
        for (var i = 0; i < messageCount; i++)
        {
            // One MessageId, several ConsumerTypes — the shape that made the compound-key claim
            // select a cross product.
            var messageId = MessageId.New();

            for (var c = 0; c < consumerCount; c++)
            {
                var message = new InboxMessage
                {
                    RowId = Guid.NewGuid(),
                    MessageId = messageId,
                    ConsumerType = Consumers[c],
                    TenantId = "tenant-a",
                    EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
                    Payload = "{}",
                    Status = InboxMessageStatus.Received,
                    ReceivedAtUtc = Now.AddSeconds(-messageCount + i),
                };

                rowIds.Add(message.RowId);
                context.InboxMessages.Add(message);
            }
        }

        await context.SaveChangesAsync();
        return rowIds;
    }
}
