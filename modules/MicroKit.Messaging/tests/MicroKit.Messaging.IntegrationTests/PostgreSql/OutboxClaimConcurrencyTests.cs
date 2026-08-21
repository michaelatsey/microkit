using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The claim under a real PostgreSQL server. This suite exists to prove the one assumption the
/// whole design rests on and that SQLite cannot test: that under <c>READ COMMITTED</c> — the
/// PostgreSQL default — a blocked <c>UPDATE</c> re-evaluates its <c>WHERE</c> against the committed
/// row version once the lock is released, so the eligibility predicate replayed inside the claim
/// rejects the loser correctly. That is what makes the token claim safe without
/// <c>FOR UPDATE SKIP LOCKED</c>.
/// </summary>
/// <remarks>
/// <c>EfOutboxStore</c> is provider-agnostic by design, so these are the same tests under a second
/// provider rather than a second implementation. Skipped, not failed, when Docker is absent.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class OutboxClaimConcurrencyTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [DockerRequiredFact]
    public async Task ClaimBatchAsync_UnderRealConcurrency_NeverOverlapsAndNeverLoses()
    {
        const int MessageCount = 200;
        const int ProcessorCount = 8;
        const int BatchSize = 10;

        await using var schema = await NewDatabaseAsync();
        var seeded = await SeedAsync(schema.ConnectionString, MessageCount);

        // Every processor gets its own DbContext, hence its own connection: the point is genuine
        // server-side contention, not interleaved calls on one connection.
        var claimed = new System.Collections.Concurrent.ConcurrentBag<MessageId>();

        var processors = Enumerable.Range(0, ProcessorCount).Select(async _ =>
        {
            await using var context = NewContext(schema.ConnectionString);
            var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

            while (true)
            {
                var claim = await store.ClaimBatchAsync(BatchSize, Lease);
                if (claim.Count == 0)
                {
                    return;
                }

                foreach (var message in claim.Messages)
                {
                    claimed.Add(message.Id);
                }

                // Settle as Published so the rows leave the eligible set and the loop terminates.
                await store.ApplyOutcomesAsync(
                    claim.Token,
                    claim.Messages.Select(m => OutboxOutcome.Published(m.Id)).ToList());
            }
        });

        await Task.WhenAll(processors);

        var claimedList = claimed.ToList();
        claimedList.Count.ShouldBe(
            MessageCount, "zero overlap: no row may be claimed by two processors");
        claimedList.ToHashSet().Count.ShouldBe(
            MessageCount, "zero loss: every row must be claimed exactly once");
        claimedList.ToHashSet().SetEquals(seeded).ShouldBeTrue();

        await using var probe = NewContext(schema.ConnectionString);
        (await probe.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Published))
            .ShouldBe(MessageCount);
    }

    [DockerRequiredFact]
    public async Task ClaimBatchAsync_WhenTwoProcessorsRaceOnOneRow_ExactlyOneWins()
    {
        // The narrowest form of the same guarantee: one eligible row, N processors, one winner.
        // This is the case where the blocked UPDATE must re-read the committed row and find it
        // no longer eligible.
        const int ProcessorCount = 16;

        await using var schema = await NewDatabaseAsync();
        await SeedAsync(schema.ConnectionString, 1);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var races = Enumerable.Range(0, ProcessorCount).Select(async _ =>
        {
            await using var context = NewContext(schema.ConnectionString);
            var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

            await start.Task;
            return await store.ClaimBatchAsync(10, Lease);
        }).ToList();

        start.SetResult();
        var claims = await Task.WhenAll(races);

        claims.Count(c => c.Count == 1).ShouldBe(1, "exactly one processor may win the row");
        claims.Count(c => c.Count == 0).ShouldBe(ProcessorCount - 1);
    }

    [DockerRequiredFact]
    public async Task ApplyOutcomesAsync_WithStaleToken_IsRejectedByTheTokenGuard()
    {
        await using var schema = await NewDatabaseAsync();
        await SeedAsync(schema.ConnectionString, 1);

        await using var contextA = NewContext(schema.ConnectionString);
        var storeA = new EfOutboxStore<TestMessagingDbContext>(contextA, new FakeTimeProvider(Now));
        var staleClaim = await storeA.ClaimBatchAsync(10, Lease);
        staleClaim.Count.ShouldBe(1);
        var messageId = staleClaim.Messages[0].Id;

        // Processor A stalls past its lease; processor B legitimately takes the message over.
        await using var contextB = NewContext(schema.ConnectionString);
        var storeB = new EfOutboxStore<TestMessagingDbContext>(
            contextB, new FakeTimeProvider(Now.Add(Lease).AddMinutes(1)));
        var freshClaim = await storeB.ClaimBatchAsync(10, Lease);
        freshClaim.Count.ShouldBe(1, "an expired lease must be re-claimable");
        freshClaim.Token.ShouldNotBe(staleClaim.Token);

        // A now finishes its dispatch and tries to settle under a token it no longer owns.
        var written = await storeA.ApplyOutcomesAsync(
            staleClaim.Token, [OutboxOutcome.Published(messageId)]);

        written.ShouldBe(0, "a lost lease must yield zero rows, never a silent overwrite");

        await using var probe = NewContext(schema.ConnectionString);
        var row = await probe.OutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(OutboxMessageStatus.Processing, "processor B still owns the row");
        row.ClaimToken.ShouldBe(freshClaim.Token);
    }

    [DockerRequiredFact]
    public async Task ClaimBatchAsync_TranslatesTheMessageIdContainsFilter()
    {
        // INTEGRATION.md flags this explicitly: the claim filters with a strongly-typed
        // MessageId inside Contains, and whether that translates depends on the value-converter
        // configuration. Confirm against the real provider rather than assuming.
        await using var schema = await NewDatabaseAsync();
        await SeedAsync(schema.ConnectionString, 12);

        await using var context = NewContext(schema.ConnectionString);
        var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

        var claim = await store.ClaimBatchAsync(12, Lease);

        claim.Count.ShouldBe(12, "a client-side evaluation or a broken IN list would not claim all 12");
    }

    // ---------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------

    private static TestMessagingDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    /// <summary>Creates a database unique to one test, so tests never see each other's rows.</summary>
    private async Task<TestDatabase> NewDatabaseAsync()
    {
        var name = $"outbox_{Guid.NewGuid():N}";
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var adminConnectionString = builder.ConnectionString;
        builder.Database = name;

        await using (var admin = new Npgsql.NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync();
        }

        var database = new TestDatabase(builder.ConnectionString, adminConnectionString, name);

        await using var context = NewContext(database.ConnectionString);
        await context.Database.EnsureCreatedAsync();

        return database;
    }

    private static async Task<HashSet<MessageId>> SeedAsync(string connectionString, int count)
    {
        await using var context = NewContext(connectionString);

        var ids = new HashSet<MessageId>();
        for (var i = 0; i < count; i++)
        {
            var message = new OutboxMessage
            {
                Id = MessageId.New(),
                TenantId = "tenant-a",
                EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
                Payload = "{}",
                Status = OutboxMessageStatus.Pending,
                OccurredOnUtc = Now.AddSeconds(-count + i),
                CreatedAtUtc = Now,
                CorrelationId = CorrelationId.New(),
            };

            ids.Add(message.Id);
            context.OutboxMessages.Add(message);
        }

        await context.SaveChangesAsync();
        return ids;
    }

    private sealed class TestDatabase(string connectionString, string adminConnectionString, string name)
        : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public async ValueTask DisposeAsync()
        {
            Npgsql.NpgsqlConnection.ClearAllPools();

            await using var admin = new Npgsql.NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
