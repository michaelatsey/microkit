using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The claim that leaves the tenants in back-off out (#124 A7), shown on PostgreSQL: its SQL — one
/// array parameter, one text whatever the number of tenants, nothing added for none — and its plan, by
/// the buffers it touches.
/// </summary>
/// <remarks>
/// Unlike an endpoint's (EndpointExclusionPlanTests), a tenant's exclusion is a residual filter: no
/// index is ordered by tenant within the claim's order, so the claim reads past the excluded tenant's
/// claimable rows, and writes none of them. Before A7 the same backlog was claimed and deferred, and the
/// claim then read past it as waiting rows: each test measures that baseline on a second database
/// seeded with the backlog already deferred, and A7 must read no more. The outbox also does not walk
/// its published history; at this size the inbox claim scans its table, before A7 as after.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class TenantExclusionPlanTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private const int Backlog = 20_000;
    private const int History = 20_000;
    private const int Wanted = 3;

    /// <summary>
    /// The down local endpoint's backlog, larger than the held tenant's: with the two the same size, a
    /// scan of the table costs the planner about what the transport branch's walk past the held tenant
    /// does, and it may take either.
    /// </summary>
    private const int LocalBacklog = Backlog * 3;
    private static readonly string[] ThreeTenants = ["A", "B", "C"];

    [DockerRequiredFact]
    public async Task Outbox_TheTenantFilter_IsOneArrayParameter_OneSqlText_AndNothingForAnEmptyList()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var none = await OutboxQueryAsync(db, [], []);
        var one = await OutboxQueryAsync(db, ["A"], []);
        var three = await OutboxQueryAsync(db, ThreeTenants, []);
        var local = await OutboxQueryAsync(db, [], ["A"]);
        output.WriteLine(none.Text);
        output.WriteLine(one.Text);
        output.WriteLine(local.Text);

        Where(none).ShouldNotContain("TenantId", Case.Sensitive, "an empty list adds no predicate: #188's SQL");
        none.Parameters.ShouldNotContain(p => p.Name.Contains("tenants", StringComparison.Ordinal));
        three.Text.ShouldBe(one.Text, "one SQL text whatever the number of tenants");
        three.Parameters.Count(p => p.Value is string[]).ShouldBe(1, "one array parameter");
        three.Parameters.Single(p => p.Value is string[]).Value.ShouldBe(ThreeTenants);
        Where(one).ShouldContain("\"TenantId\" IS NULL", Case.Sensitive, "a row with no tenant is never left out");
    }

    [DockerRequiredFact]
    public async Task Inbox_TheTenantFilter_IsOneArrayParameter_OneSqlText_AndNothingForAnEmptyList()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var none = await InboxQueryAsync(db, []);
        var one = await InboxQueryAsync(db, ["A"]);
        var three = await InboxQueryAsync(db, ["A", "B", "C"]);
        output.WriteLine(one.Text);

        Where(none).ShouldNotContain("TenantId", Case.Sensitive, "an empty list adds no predicate");
        three.Text.ShouldBe(one.Text, "one SQL text whatever the number of tenants");
        three.Parameters.Count(p => p.Value is string[]).ShouldBe(1, "one array parameter");
        Where(one).ShouldContain("\"TenantId\" IS NULL", Case.Sensitive, "a row with no tenant is never left out");
    }

    [DockerRequiredFact]
    public Task Outbox_AOneTenantStore_ClaimsAndWritesNothing_ReadingNoMoreThanBeforeA7_NorTheHistory()
        => OutboxPlanAsync(otherTenant: null);

    [DockerRequiredFact]
    public Task Outbox_AnExcludedTenantsBacklogAhead_TheOtherTenantsRowsAreClaimed_ReadingNoMoreThanBeforeA7_NorTheHistory()
        => OutboxPlanAsync(otherTenant: "B");

    [DockerRequiredFact]
    public async Task Inbox_AOneTenantStore_ClaimsAndWritesNothing_ReadingNoMoreThanBeforeA7()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedInboxAsync(db, [
            .. Enumerable.Range(0, Backlog).Select(i => InboxRow(i, InboxMessageStatus.Failed)),
            .. Enumerable.Range(0, History).Select(i => InboxRow(Backlog + i, InboxMessageStatus.Processed)),
        ]);
        var before = await SnapshotAsync(db, "InboxMessages");
        var excluding = await ExplainAsync(db.ConnectionString, await InboxQueryAsync(db, ["A"]));
        (await SnapshotAsync(db, "InboxMessages")).ShouldBe(before, "the excluded tenant's rows are neither claimed nor written");

        await using var deferredDb = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedInboxAsync(deferredDb, [
            .. Enumerable.Range(0, Backlog).Select(i => InboxRow(i, InboxMessageStatus.Failed, deferred: true)),
            .. Enumerable.Range(0, History).Select(i => InboxRow(Backlog + i, InboxMessageStatus.Processed)),
        ]);
        var beforeA7 = await ExplainAsync(deferredDb.ConnectionString, await InboxQueryAsync(deferredDb, []));

        output.WriteLine($"excluding A: {excluding} buffers; the same backlog deferred, no exclusion (before A7): {beforeA7}");
        excluding.ShouldBeLessThanOrEqualTo(Margin(beforeA7));
    }

    /// <summary>
    /// The local endpoint down and a tenant held on the transport at once: the tenant filter sits in each
    /// branch of the endpoint exclusion, and the local backlog is still not walked (#188's bound).
    /// </summary>
    [DockerRequiredFact]
    public async Task Outbox_TheLocalEndpointExcluded_AndATransportTenantExcluded_TheLocalBacklogIsNotWalked()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedOutboxAsync(db, [
            .. Enumerable.Range(0, Backlog).Select(i => OutboxRow("A", History + i, OutboxMessageStatus.Pending)),
            .. Enumerable.Range(0, Wanted).Select(i => OutboxRow("B", History + Backlog + i, OutboxMessageStatus.Pending)),
        ]);
        var withoutLocal = await ExplainAsync(db.ConnectionString, await LocalAndTenantAsync(db));
        var pagesBefore = await HeapPagesAsync(db);

        await SeedOutboxAsync(db, Enumerable.Range(0, LocalBacklog).Select(i =>
        {
            var row = OutboxRow("B", i - LocalBacklog, OutboxMessageStatus.Pending);
            row.Endpoint = OutboxMessage.LocalEndpoint;
            row.ContractName = null;
            row.Source = null;
            return row;
        }));
        var withLocal = await ExplainAsync(db.ConnectionString, await LocalAndTenantAsync(db));
        var localPages = await HeapPagesAsync(db) - pagesBefore;

        // The transport branch reads past A's rows either way; what the local backlog may add is index
        // pages, never its heap.
        output.WriteLine(
            $"local and A excluded: {withoutLocal} buffers, {withLocal} with {LocalBacklog} local rows ahead "
            + $"({localPages} heap pages)");
        localPages.ShouldBeGreaterThan(400, "control: the local backlog is large enough to tell a walk");
        (withLocal - withoutLocal).ShouldBeLessThan(localPages / 4, "the down local endpoint's backlog is not walked");

        static async Task<CapturedCommand> LocalAndTenantAsync(StoreDatabase db)
        {
            var capture = new CandidateQueryCapture();
            await using var context = NewContext(db.ConnectionString, capture);
            var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
            capture.Arm();
            var claim = await store.ClaimBatchAsync(100, TimeSpan.FromMinutes(5), OutboxEndpoints.Local, ["A"], []);
            claim.Messages.ShouldAllBe(m => m.TenantId == "B" && m.Endpoint == null);
            claim.Count.ShouldBe(Wanted);
            await store.ApplyOutcomesAsync(claim.Token, [.. claim.Messages.Select(m => OutboxOutcome.Released(m.Id))]);
            return capture.Candidates.ShouldNotBeNull();
        }
    }

    private static async Task<int> HeapPagesAsync(StoreDatabase db)
    {
        await using var connection = new Npgsql.NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT relpages FROM pg_class WHERE relname = 'OutboxMessages'";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task OutboxPlanAsync(string? otherTenant)
    {
        // The excluded tenant's backlog, heading the queue; then the few rows of another tenant the claim
        // must reach, if any; then — added in a second step — the published history, older than both.
        var wanted = otherTenant is null ? 0 : Wanted;
        IEnumerable<OutboxMessage> Waiting(bool deferred) =>
        [
            .. Enumerable.Range(0, Backlog).Select(i => OutboxRow("A", History + i, OutboxMessageStatus.Pending, deferred)),
            .. Enumerable.Range(0, wanted).Select(i => OutboxRow(otherTenant, History + Backlog + i, OutboxMessageStatus.Pending)),
        ];
        IEnumerable<OutboxMessage> HistoryRows() =>
            Enumerable.Range(0, History).Select(i => OutboxRow(otherTenant ?? "A", i, OutboxMessageStatus.Published));

        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedOutboxAsync(db, Waiting(deferred: false));
        var before = await SnapshotAsync(db, "OutboxMessages", tenant: "A");
        var backlogOnly = await ExplainAsync(
            db.ConnectionString, await OutboxQueryAsync(db, ["A"], [], expectedClaims: wanted, expectedTenant: otherTenant));

        await SeedOutboxAsync(db, HistoryRows());
        var excluding = await ExplainAsync(
            db.ConnectionString, await OutboxQueryAsync(db, ["A"], [], expectedClaims: wanted, expectedTenant: otherTenant));
        (await SnapshotAsync(db, "OutboxMessages", tenant: "A", waitingOnly: true)).ShouldBe(
            before, "the excluded tenant's rows are neither claimed nor written");

        await using var deferredDb = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedOutboxAsync(deferredDb, [.. HistoryRows(), .. Waiting(deferred: true)]);
        var beforeA7 = await ExplainAsync(
            deferredDb.ConnectionString, await OutboxQueryAsync(deferredDb, [], [], expectedClaims: wanted, expectedTenant: otherTenant));

        output.WriteLine(
            $"excluding A: {backlogOnly} buffers, {excluding} with {History} published rows; "
            + $"the same backlog deferred, no exclusion (before A7): {beforeA7}");
        excluding.ShouldBeLessThanOrEqualTo(Margin(backlogOnly), "the published history is not walked");
        excluding.ShouldBeLessThanOrEqualTo(Margin(beforeA7), "no more than the claim read over the deferred backlog before A7");
    }

    private static async Task SeedOutboxAsync(StoreDatabase db, IEnumerable<OutboxMessage> rows)
    {
        await using var seed = NewContext(db.ConnectionString);
        seed.OutboxMessages.AddRange(rows);
        await seed.SaveChangesAsync();
        await seed.Database.ExecuteSqlRawAsync("VACUUM ANALYZE \"OutboxMessages\"");
    }

    private static async Task SeedInboxAsync(StoreDatabase db, IEnumerable<InboxMessage> rows)
    {
        await using var seed = NewContext(db.ConnectionString);
        seed.InboxMessages.AddRange(rows);
        await seed.SaveChangesAsync();
        await seed.Database.ExecuteSqlRawAsync("VACUUM ANALYZE \"InboxMessages\"");
    }

    /// <summary>Two plans of one table differ by a few pages of index and sort.</summary>
    private static int Margin(int baseline) => baseline + (baseline / 10) + 10;

    /// <summary>
    /// Runs one claim and captures its candidate query, then releases what it claimed so the next query
    /// sees the rows as they were.
    /// </summary>
    private static async Task<CapturedCommand> OutboxQueryAsync(
        StoreDatabase db, string[] transportTenants, string[] localTenants, int expectedClaims = 0, string? expectedTenant = null)
    {
        var capture = new CandidateQueryCapture();
        await using var context = NewContext(db.ConnectionString, capture);
        var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        capture.Arm();
        var claim = await store.ClaimBatchAsync(100, TimeSpan.FromMinutes(5), OutboxEndpoints.None, transportTenants, localTenants);
        claim.Count.ShouldBe(expectedClaims);
        claim.Messages.ShouldAllBe(m => m.TenantId == expectedTenant);
        if (claim.Count > 0)
        {
            await store.ApplyOutcomesAsync(claim.Token, [.. claim.Messages.Select(m => OutboxOutcome.Released(m.Id))]);
        }

        return capture.Candidates.ShouldNotBeNull();
    }

    private static async Task<CapturedCommand> InboxQueryAsync(StoreDatabase db, string[] tenants, int expectedClaims = 0)
    {
        var capture = new CandidateQueryCapture();
        await using var context = NewContext(db.ConnectionString, capture);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        capture.Arm();
        var claim = await store.ClaimBatchAsync(100, TimeSpan.FromMinutes(5), tenants);
        claim.Count.ShouldBe(expectedClaims);
        return capture.Candidates.ShouldNotBeNull();
    }

    private static string Where(CapturedCommand query)
        => query.Text[query.Text.IndexOf("WHERE", StringComparison.Ordinal)..];

    /// <summary>
    /// What a claim or a deferral writes on the rows, as one comparable string; with
    /// <paramref name="waitingOnly"/>, on the rows not yet published or processed.
    /// </summary>
    private static async Task<string> SnapshotAsync(StoreDatabase db, string table, string? tenant = null, bool waitingOnly = false)
    {
        var order = table == "OutboxMessages" ? "\"CreatedAtUtc\"" : "\"ReceivedAtUtc\"";
        var filters = new List<string>();
        if (tenant is not null)
        {
            filters.Add("\"TenantId\" = @tenant");
        }

        if (waitingOnly)
        {
            filters.Add("\"Status\" NOT IN ('Published', 'Processed')");
        }

        await using var connection = new Npgsql.NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The table and the filters are the test's own constants; the tenant is a parameter.
        command.CommandText =
            "SELECT md5(string_agg(concat_ws('|', \"Status\", \"NextRetryAtUtc\", \"ClaimToken\", \"LockedUntilUtc\", \"RetryCount\"), ','"
            + $" ORDER BY {order})) FROM \"{table}\"" + (filters.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", filters));
#pragma warning restore CA2100
        if (tenant is not null)
        {
            command.Parameters.Add(new Npgsql.NpgsqlParameter("tenant", tenant));
        }

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> ExplainAsync(string connectionString, CapturedCommand query)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query.Text;
        foreach (var (name, value) in query.Parameters)
        {
            command.Parameters.Add(new Npgsql.NpgsqlParameter(name, value));
        }

        var json = (string)(await command.ExecuteScalarAsync())!;
        output.WriteLine(json);
        using var plan = JsonDocument.Parse(json);
        var top = plan.RootElement[0].GetProperty("Plan");
        return top.GetProperty("Shared Hit Blocks").GetInt32() + top.GetProperty("Shared Read Blocks").GetInt32();
    }

    private static TestMessagingDbContext NewContext(string connectionString, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new TestMessagingDbContext(options.Options);
    }

    private static OutboxMessage OutboxRow(string? tenant, int order, OutboxMessageStatus status, bool deferred = false) => new()
    {
        NextRetryAtUtc = deferred ? Now.AddMinutes(5) : null,
        Id = MessageId.New(),
        ContractName = "tests.plan.v1",
        Source = "tests.plan",
        TenantId = tenant,
        EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
        Payload = "{}",
        Status = status,
        ProcessedAtUtc = status is OutboxMessageStatus.Published ? Now : null,
        OccurredOnUtc = Now,
        CreatedAtUtc = Now.AddMilliseconds(order - ((Backlog + History) * 2)),
        CorrelationId = CorrelationId.New(),
    };

    private static InboxMessage InboxRow(int order, InboxMessageStatus status, bool deferred = false) => new()
    {
        MessageId = MessageId.New(),
        ConsumerType = "Tests.PlanHandler, Tests",
        TenantId = "A",
        EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
        Payload = "{}",
        Source = "tests.plan",
        Status = status,
        RetryCount = 1,
        NextRetryAtUtc = status is InboxMessageStatus.Failed ? (deferred ? Now.AddMinutes(5) : Now.AddMinutes(-1)) : null,
        ProcessedAtUtc = status is InboxMessageStatus.Processed ? Now : null,
        ReceivedAtUtc = Now.AddMilliseconds(order - ((Backlog + History) * 2)),
        OccurredOnUtc = Now,
    };

    private sealed record CapturedCommand(string Text, List<(string Name, object? Value)> Parameters);

    /// <summary>Captures the claim's candidate query — the SELECT the claim orders and limits.</summary>
    private sealed class CandidateQueryCapture : DbCommandInterceptor
    {
        private bool _armed;

        public CapturedCommand? Candidates { get; private set; }

        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && Candidates is null && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                Candidates = new CapturedCommand(
                    command.CommandText,
                    [.. command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value))]);
            }

            return ValueTask.FromResult(result);
        }
    }
}
