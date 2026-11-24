using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The claim's tenant filters (#124 A7), on PostgreSQL, under both null semantics a host's context may
/// choose: a row with no tenant is never left out — <c>UseRelationalNulls()</c> turns a plain
/// <c>NOT IN</c> over the nullable column into a filter that leaves it out — and each endpoint's list
/// leaves out only that endpoint's rows of the listed tenants. The inbox's lists are a (consumer, tenant)
/// pair's and a consumer's (#210): a consumer left out for every tenant still has its rows with no tenant
/// claimed.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class TenantClaimFilterTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [DockerRequiredFact]
    public Task Outbox_EachEndpointsTenantList_LeavesOutOnlyThatEndpointsRowsOfThoseTenants_NeverARowWithNoTenant()
        => OutboxAsync(relationalNulls: false);

    [DockerRequiredFact]
    public Task Outbox_UnderRelationalNulls_ARowWithNoTenant_IsStillNeverLeftOut()
        => OutboxAsync(relationalNulls: true);

    [DockerRequiredFact]
    public Task Inbox_AnExcludedPair_LeavesOutThatPairsRows_NeverARowWithNoTenant()
        => InboxAsync(relationalNulls: false, consumerWide: false, expected: ["-", "B"]);

    [DockerRequiredFact]
    public Task Inbox_UnderRelationalNulls_ARowWithNoTenant_IsStillNeverLeftOut()
        => InboxAsync(relationalNulls: true, consumerWide: false, expected: ["-", "B"]);

    [DockerRequiredFact]
    public Task Inbox_AnExcludedConsumer_LeavesOutItsRowsWithATenant_NeverItsRowWithNoTenant()
        => InboxAsync(relationalNulls: false, consumerWide: true, expected: ["-"]);

    [DockerRequiredFact]
    public Task Inbox_AnExcludedConsumer_UnderRelationalNulls_StillClaimsItsRowWithNoTenant()
        => InboxAsync(relationalNulls: true, consumerWide: true, expected: ["-"]);

    private async Task OutboxAsync(bool relationalNulls)
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var rows = new Dictionary<string, OutboxMessage>(StringComparer.Ordinal);
        var order = 0;
        foreach (var endpoint in new[] { "transport", "local" })
        {
            foreach (var tenant in new[] { "A", "B", "-" })
            {
                rows[$"{endpoint}:{tenant}"] = new OutboxMessage
                {
                    Id = MessageId.New(),
                    Endpoint = endpoint == "local" ? OutboxMessage.LocalEndpoint : null,
                    ContractName = endpoint == "local" ? null : "tests.filter.v1",
                    Source = endpoint == "local" ? null : "tests.filter",
                    TenantId = tenant == "-" ? null : tenant,
                    EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
                    Payload = "{}",
                    Status = OutboxMessageStatus.Pending,
                    OccurredOnUtc = Now,
                    CreatedAtUtc = Now.AddSeconds(++order - 100),
                    CorrelationId = CorrelationId.New(),
                };
            }
        }

        await using (var seed = NewContext(db, relationalNulls))
        {
            seed.OutboxMessages.AddRange(rows.Values);
            await seed.SaveChangesAsync();
        }

        async Task<string[]> ClaimAsync(OutboxEndpoints endpoints, string[] transport, string[] local)
        {
            await using var context = NewContext(db, relationalNulls);
            var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
            var claim = await store.ClaimBatchAsync(100, Lease, endpoints, transport, local);
            await store.ApplyOutcomesAsync(claim.Token, [.. claim.Messages.Select(m => OutboxOutcome.Released(m.Id))]);
            return [.. rows.Where(r => claim.Messages.Any(m => m.Id == r.Value.Id)).Select(r => r.Key).Order(StringComparer.Ordinal)];
        }

        (await ClaimAsync(OutboxEndpoints.None, [], [])).Length.ShouldBe(6, "control: every row is claimable");
        (await ClaimAsync(OutboxEndpoints.None, ["A"], [])).ShouldBe(
            ["local:-", "local:A", "local:B", "transport:-", "transport:B"], "A's transport row only");
        (await ClaimAsync(OutboxEndpoints.None, [], ["A"])).ShouldBe(
            ["local:-", "local:B", "transport:-", "transport:A", "transport:B"], "A's local row only");
        (await ClaimAsync(OutboxEndpoints.None, ["A", "B"], ["A"])).ShouldBe(
            ["local:-", "local:B", "transport:-"], "the rows with no tenant are never left out");
        (await ClaimAsync(OutboxEndpoints.Transport, [], ["B"])).ShouldBe(
            ["local:-", "local:A"], "with the transport left out, B's local row too");
    }

    private async Task InboxAsync(bool relationalNulls, bool consumerWide, string[] expected)
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var order = 0;
        var rows = new[] { "A", "B", null }.ToDictionary(t => t ?? "-", t => new InboxMessage
        {
            MessageId = MessageId.New(),
            ConsumerType = "Tests.FilterHandler, Tests",
            TenantId = t,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Source = "tests.filter",
            Status = InboxMessageStatus.Failed,
            RetryCount = 1,
            ReceivedAtUtc = Now.AddSeconds(++order - 100),
            OccurredOnUtc = Now,
        });
        await using (var seed = NewContext(db, relationalNulls))
        {
            seed.InboxMessages.AddRange(rows.Values);
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext(db, relationalNulls);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        const string consumer = "Tests.FilterHandler, Tests";
        var excluded = consumerWide
            ? new InboxClaimExclusions([consumer], [])
            : new InboxClaimExclusions([], [(consumer, "A")]);
        var claim = await store.ClaimBatchAsync(100, Lease, excluded);

        claim.Messages.Select(m => m.TenantId ?? "-").Order(StringComparer.Ordinal).ShouldBe(expected);
    }

    private static TestMessagingDbContext NewContext(StoreDatabase db, bool relationalNulls)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>()
            .UseNpgsql(db.ConnectionString, o => o.UseRelationalNulls(relationalNulls))
            .Options);
}
