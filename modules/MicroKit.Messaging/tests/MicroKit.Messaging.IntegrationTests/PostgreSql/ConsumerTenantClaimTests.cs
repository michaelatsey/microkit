using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// V9 (#210 D6): the inbox claim leaves out the excluded (consumer, tenant) pairs and consumers on PostgreSQL,
/// matching a pair on <c>ConsumerType + U+001F + TenantId</c>: an excluded pair's rows are neither claimed nor
/// written, the same tenant's rows of another consumer are claimed, and the match is exact — for a tenant id
/// containing U+001F, and for pairs whose joins without a separator would collide. A consumer excluded for
/// every tenant, and the rows with no tenant, are <see cref="TenantClaimFilterTests"/>'s.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class ConsumerTenantClaimTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private const string X = "Tests.ClaimX, Tests";
    private const string Y = "Tests.ClaimY, Tests";
    private const string Separator = "\u001F";

    [DockerRequiredFact]
    public async Task AnExcludedPair_IsNeitherClaimedNorWritten_AndTheSameTenantsRowsOfAnotherConsumer_AreClaimed()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedAsync(db, (X, "A"), (X, "A"), (Y, "A"), (Y, "A"), (X, "B"));
        var before = await SnapshotAsync(db, X, "A");

        var claimed = await ClaimAsync(db, new InboxClaimExclusions([], [(X, "A")]));

        claimed.ShouldBe([$"{X}|B", $"{Y}|A", $"{Y}|A"], "X's rows of A only are left out");
        (await SnapshotAsync(db, X, "A")).ShouldBe(before, "the excluded pair's rows are neither claimed nor written");
    }

    [DockerRequiredFact]
    public async Task ATenantIdContainingTheSeparator_IsMatchedExactly()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedAsync(db, (X, "A"), (X, $"A{Separator}B"), (X, Separator));

        (await ClaimAsync(db, new InboxClaimExclusions([], [(X, "A")])))
            .ShouldBe([$"{X}|{Separator}", $"{X}|A{Separator}B"], "a tenant id is the key's suffix: anything goes");
        (await ClaimAsync(db, new InboxClaimExclusions([], [(X, $"A{Separator}B")])))
            .ShouldBe([$"{X}|{Separator}", $"{X}|A"]);
        (await ClaimAsync(db, new InboxClaimExclusions([], [(X, Separator)])))
            .ShouldBe([$"{X}|A", $"{X}|A{Separator}B"]);
    }

    /// <summary>
    /// An empty tenant id is a tenant (PR #219 review): a pair holding it leaves out that consumer's rows with an
    /// empty tenant id only — never its rows with no tenant, nor another consumer's.
    /// </summary>
    [DockerRequiredFact]
    public async Task APairWithAnEmptyTenantId_LeavesOutThatPairOnly_NeverARowWithNoTenant()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedAsync(db, (X, string.Empty), (X, null!), (Y, string.Empty), (X, "A"));

        (await ClaimAsync(db, new InboxClaimExclusions([], [(X, string.Empty)])))
            .ShouldBe([$"{X}|<null>", $"{X}|A", $"{Y}|"]);
    }

    /// <remarks>
    /// Joined with no separator, ("K", "AB") and ("KA", "B") are both <c>KAB</c>. With it they differ, since a
    /// key's first separator sits at its consumer's length. The variant that would collide with it —
    /// ("K" + U+001F + "A", "B") against ("K", "A" + U+001F + "B") — needs a consumer key containing U+001F, which
    /// <see cref="MessageConsumerKey.Of"/> and <see cref="LocalConsumer"/> refuse (V10).
    /// </remarks>
    [DockerRequiredFact]
    public async Task TwoPairsWhoseJoinsWithoutASeparatorCollide_AreToldApart()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedAsync(db, ("K", "AB"), ("KA", "B"));

        (await ClaimAsync(db, new InboxClaimExclusions([], [("KA", "B")]))).ShouldBe(["K|AB"]);
        (await ClaimAsync(db, new InboxClaimExclusions([], [("K", "AB")]))).ShouldBe(["KA|B"]);
        (await ClaimAsync(db, new InboxClaimExclusions(["K"], []))).ShouldBe(["KA|B"], "control: the consumer list is exact too");
    }

    /// <summary>
    /// The EF store joins a pair with the character the consumer key refuses: the two cannot drift apart
    /// (commit 1 review of #210). The EF assembly cannot reference <c>MessageConsumerKey</c>'s constant.
    /// </summary>
    [Fact]
    public void TheClaimsSeparator_IsTheCharacterAConsumerKeyRefuses()
    {
        EfInboxStore<TestMessagingDbContext>.PairSeparator.ShouldBe(Separator);
        Should.Throw<ArgumentException>(() => new LocalConsumer(
            typeof(object), $"Tests.Consumer{EfInboxStore<TestMessagingDbContext>.PairSeparator}", typeof(object),
            static (_, _, _, _) => ValueTask.CompletedTask));
    }

    private static async Task SeedAsync(StoreDatabase db, params (string Consumer, string Tenant)[] rows)
    {
        await using var context = NewContext(db);
        var order = 0;
        context.InboxMessages.AddRange(rows.Select(r => new InboxMessage
        {
            MessageId = MessageId.New(),
            ConsumerType = r.Consumer,
            TenantId = r.Tenant,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Source = "tests.claim",
            Status = InboxMessageStatus.Failed,
            RetryCount = 1,
            ReceivedAtUtc = Now.AddSeconds(++order - 100),
            OccurredOnUtc = Now,
        }));
        await context.SaveChangesAsync();
    }

    /// <summary>One claim, its rows as "consumer|tenant" in order; then released, so the next claim sees them again.</summary>
    private static async Task<List<string>> ClaimAsync(StoreDatabase db, InboxClaimExclusions excluded)
    {
        await using var context = NewContext(db);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        var claim = await store.ClaimBatchAsync(100, Lease, excluded);
        if (claim.Count > 0)
        {
            (await store.ApplyOutcomesAsync(claim.Token, [.. claim.Messages.Select(m => InboxOutcome.Released(new InboxMessageKey(m.MessageId, m.ConsumerType)))]))
                .ShouldBe(claim.Count);
        }

        return [.. claim.Messages.Select(m => $"{m.ConsumerType}|{m.TenantId ?? "<null>"}").Order(StringComparer.Ordinal)];
    }

    /// <summary>What a claim or a settlement writes on a pair's rows, as one comparable string.</summary>
    private static async Task<string> SnapshotAsync(StoreDatabase db, string consumer, string tenant)
    {
        await using var context = NewContext(db);
        var rows = await context.InboxMessages.AsNoTracking()
            .Where(m => m.ConsumerType == consumer && m.TenantId == tenant)
            .OrderBy(m => m.RowId)
            .ToListAsync();
        return string.Join(";", rows.Select(r => $"{r.RowId}|{r.Status}|{r.ClaimToken}|{r.LockedUntilUtc:O}|{r.NextRetryAtUtc:O}|{r.RetryCount}"));
    }

    private static TestMessagingDbContext NewContext(StoreDatabase db)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(db.ConnectionString).Options);
}
