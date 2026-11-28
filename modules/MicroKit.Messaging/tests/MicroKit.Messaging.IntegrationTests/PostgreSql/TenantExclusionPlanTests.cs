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
    private const string PlanConsumer = "Tests.PlanHandler, Tests";
    private const string OtherConsumer = "Tests.OtherPlanHandler, Tests";

    /// <summary>
    /// The inbox claim's candidate query on main 1e0dba6, excluding tenants (#124 A7), as EF Core 10.0.9 and
    /// Npgsql 10.0.2 sent it: captured from <c>Inbox_TheTenantFilter_IsOneArrayParameter…</c> on that commit,
    /// verbatim. D11 measures it against the claim that replaced it.
    /// </summary>
    private const string MainInboxClaim =
        "SELECT i.\"RowId\"\n"
        + "FROM \"InboxMessages\" AS i\n"
        + "WHERE NOT (i.\"DeadLettered\") AND (i.\"Status\" IN ('Failed', 'Received') OR (i.\"Status\" = 'Processing' AND i.\"LockedUntilUtc\" <= @now)) AND (i.\"NextRetryAtUtc\" IS NULL OR i.\"NextRetryAtUtc\" <= @now) AND (i.\"TenantId\" IS NULL OR NOT (i.\"TenantId\" = ANY (@tenants) AND i.\"TenantId\" = ANY (@tenants) IS NOT NULL))\n"
        + "ORDER BY i.\"ReceivedAtUtc\"\n"
        + "LIMIT @p";

    /// <summary>Main's claim with nothing excluded: the same text without the tenant arm, which main added only for a list.</summary>
    private const string MainInboxClaimWithoutExclusion =
        "SELECT i.\"RowId\"\n"
        + "FROM \"InboxMessages\" AS i\n"
        + "WHERE NOT (i.\"DeadLettered\") AND (i.\"Status\" IN ('Failed', 'Received') OR (i.\"Status\" = 'Processing' AND i.\"LockedUntilUtc\" <= @now)) AND (i.\"NextRetryAtUtc\" IS NULL OR i.\"NextRetryAtUtc\" <= @now)\n"
        + "ORDER BY i.\"ReceivedAtUtc\"\n"
        + "LIMIT @p";

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

    /// <summary>
    /// V9 (#210 D6): nothing excluded gives main's SQL text, with no predicate; any exclusion — a pair, a
    /// consumer, 1, 3 or 100 pairs — one text, with two array parameters (the consumers, the pairs' keys) and
    /// the <c>TenantId IS NULL</c> arm.
    /// </summary>
    [DockerRequiredFact]
    public async Task Inbox_TheClaimFilter_IsOneSqlTextForAnyExclusion_TwoArrayParameters_AndMainsTextForNone()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var none = await InboxQueryAsync(db, InboxClaimExclusions.None);
        var one = await InboxQueryAsync(db, Pairs(1));
        var three = await InboxQueryAsync(db, Pairs(3));
        var hundred = await InboxQueryAsync(db, Pairs(100));
        var consumer = await InboxQueryAsync(db, new InboxClaimExclusions([PlanConsumer], []));
        var both = await InboxQueryAsync(db, new InboxClaimExclusions([PlanConsumer], [(OtherConsumer, "A")]));
        output.WriteLine(none.Text);
        output.WriteLine(one.Text);

        none.Text.ShouldBe(MainInboxClaimWithoutExclusion, "nothing excluded: main's SQL, no predicate");
        none.Parameters.ShouldNotContain(p => p.Value is string[]);
        foreach (var query in new[] { three, hundred, consumer, both })
        {
            query.Text.ShouldBe(one.Text, "one SQL text whatever is excluded and however much");
            query.Parameters.Count(p => p.Value is string[]).ShouldBe(2, "one array parameter per list");
        }

        Where(one).ShouldContain("\"TenantId\" IS NULL", Case.Sensitive, "a row with no tenant is never left out");
        hundred.Parameters.Select(p => p.Value).OfType<string[]>().ShouldContain(keys => keys.Length == 100);
        consumer.Parameters.Select(p => p.Value).OfType<string[]>().ShouldContain(keys => keys.Length == 0, "an empty array, not another text");
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
        var excluding = await ExplainAsync(db.ConnectionString, await InboxQueryAsync(db, Pairs(1)));
        (await SnapshotAsync(db, "InboxMessages")).ShouldBe(before, "the excluded tenant's rows are neither claimed nor written");

        await using var deferredDb = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await SeedInboxAsync(deferredDb, [
            .. Enumerable.Range(0, Backlog).Select(i => InboxRow(i, InboxMessageStatus.Failed, deferred: true)),
            .. Enumerable.Range(0, History).Select(i => InboxRow(Backlog + i, InboxMessageStatus.Processed)),
        ]);
        var beforeA7 = await ExplainAsync(deferredDb.ConnectionString, await InboxQueryAsync(deferredDb, InboxClaimExclusions.None));

        output.WriteLine($"excluding A: {excluding} buffers; the same backlog deferred, no exclusion (before A7): {beforeA7}");
        excluding.ShouldBeLessThanOrEqualTo(Margin(beforeA7));
    }

    /// <summary>
    /// D11 (#210): the claim that leaves a (consumer, tenant) pair out, against main's claim that left the
    /// tenant out (<see cref="MainInboxClaim"/>), at equal exclusion — the held pair is the backlog's only
    /// pair — and with 100 pairs, and the consumer-only shape, at the backlog size of #124's plan tests and at a
    /// tenth of it. Each query is run under <c>EXPLAIN (ANALYZE, BUFFERS)</c>, the shapes alternately, never in
    /// parallel, one warm-up round then <see cref="Rounds"/> measured; the medians are reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bound of #124 is about reads, and this asserts it: at equal exclusion the pair shape reads no more
    /// shared buffers than main's tenant shape, at both sizes. Execution time is reported, never asserted. The
    /// medians of the two runs that decided it (2026-11-24, WSL2, PostgreSQL through Testcontainers), run 1 /
    /// run 2:
    /// </para>
    /// <code>
    /// backlog 20,000        execution (ms)   shared buffers   removed by filter   rows   plan
    ///   main, tenant A      6.019 / 6.828    976 / 976        40,000              3      Limit > Sort > Seq Scan
    ///   pair (X, A)         8.325 / 9.721    976 / 976        40,000              3      Limit > Sort > Seq Scan
    ///   100 pairs           9.045 / 9.498    1,107 / 1,111    20,000              3      Limit > Sort > Bitmap Heap Scan >
    ///                                                                                      BitmapOr of two Bitmap Index Scans
    ///                                                                                      (IX_InboxMessages_Processable)
    ///   consumer X          6.476 / 7.199    976 / 976        40,003              0      Limit > Sort > Seq Scan
    /// backlog 2,000
    ///   main, tenant A      0.779 / 0.784    98 / 98          4,000               3      Limit > Sort > Seq Scan
    ///   pair (X, A)         1.012 / 0.797    98 / 98          4,000               3      Limit > Sort > Seq Scan
    ///   100 pairs           1.102 / 0.975    98 / 98          4,000               3      Limit > Sort > Seq Scan
    ///   consumer X          0.881 / 0.876    98 / 98          4,003               0      Limit > Sort > Seq Scan
    /// </code>
    /// <para>
    /// Equal reads — the same plan, the same buffers, the same rows removed — plus the key comparison per row
    /// read: Npgsql's translation evaluates <c>ConsumerType || U+001F || TenantId = ANY (@keys)</c> twice (EF Core's
    /// null compensation), about +40% execution at 20,000 held rows, within noise at 2,000. The consumer-only
    /// shape, with no concatenation, is within 5–8% of main. With 100 pairs at 20,000 rows the planner takes a
    /// bitmap scan of the processable index and reads 13% more buffers, for the same rows returned: it is not an
    /// equal exclusion — main has no 100-pair shape — and is reported, not asserted.
    /// </para>
    /// </remarks>
    [DockerRequiredFact]
    public async Task Inbox_D11_TheConsumerTenantClaim_AgainstMainsTenantClaim_AtTwoBacklogSizes()
    {
        // The texts, captured on an empty database: what the claim sends does not depend on the rows, and
        // EXPLAIN ANALYZE of the candidate SELECT writes nothing.
        await using var empty = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var pair = await InboxQueryAsync(empty, Pairs(1));
        var hundred = await InboxQueryAsync(empty, Pairs(100));
        var consumer = await InboxQueryAsync(empty, new InboxClaimExclusions([PlanConsumer], []));
        var now = pair.Parameters.Single(p => p.Name.Contains("now", StringComparison.Ordinal)).Value;
        var limit = pair.Parameters.Single(p => p.Value is int).Value;
        var main = new CapturedCommand(MainInboxClaim, [("now", now), ("tenants", MainExcludedTenants), ("p", limit)]);
        (string Name, CapturedCommand Query, int Returned)[] shapes =
        [
            ("main, tenant A", main, Wanted),
            ("pair (X, A)", pair, Wanted),
            ("100 pairs", hundred, Wanted),
            ("consumer X", consumer, 0),
        ];

        foreach (var backlog in new[] { Backlog, Backlog / 10 })
        {
            await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
            await SeedInboxAsync(db, [
                .. Enumerable.Range(0, backlog).Select(i => InboxRow(i, InboxMessageStatus.Failed)),
                .. Enumerable.Range(0, Wanted).Select(i => InboxRow(backlog + i, InboxMessageStatus.Failed, tenant: "B")),
                .. Enumerable.Range(0, backlog).Select(i => InboxRow(backlog + Wanted + i, InboxMessageStatus.Processed)),
            ]);

            var runs = shapes.Select(_ => new List<PlanStats>()).ToArray();
            for (var round = 0; round <= Rounds; round++)
            {
                for (var shape = 0; shape < shapes.Length; shape++)
                {
                    var stats = await ExplainStatsAsync(db.ConnectionString, shapes[shape].Query);
                    stats.Rows.ShouldBe(shapes[shape].Returned, $"{shapes[shape].Name}: the rows the claim reaches");
                    if (round > 0)
                    {
                        runs[shape].Add(stats);
                    }
                }
            }

            output.WriteLine($"D11, backlog {backlog} (tenant A, consumer X) + {Wanted} rows of B + {backlog} processed:");
            var medians = runs.Select(PlanStats.Median).ToArray();
            for (var shape = 0; shape < shapes.Length; shape++)
            {
                var median = medians[shape];
                output.WriteLine(
                    $"  {shapes[shape].Name,-15} execution {median.ExecutionMs,8:F3} ms  shared buffers {median.Buffers,6}  "
                    + $"removed by filter {median.RemovedByFilter,7}  rows {median.Rows}  "
                    + $"(times: {string.Join(", ", runs[shape].Select(r => r.ExecutionMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))})"
                    + $"  plan: {median.Nodes}");
            }

            // The bound (#124): at equal exclusion the pair shape reads no more than main's tenant shape.
            medians[1].Buffers.ShouldBeLessThanOrEqualTo(medians[0].Buffers, $"backlog {backlog}: the pair shape's reads");
            medians[1].RemovedByFilter.ShouldBe(medians[0].RemovedByFilter, $"backlog {backlog}: the same rows read past");
        }
    }

    /// <summary>The rounds D11 measures after its warm-up.</summary>
    private const int Rounds = 5;

    /// <summary>What main's claim excluded in D11: the backlog's tenant.</summary>
    private static readonly string[] MainExcludedTenants = ["A"];

    private static async Task<PlanStats> ExplainStatsAsync(string connectionString, CapturedCommand query)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query.Text;
        foreach (var (name, value) in query.Parameters)
        {
            command.Parameters.Add(new Npgsql.NpgsqlParameter(name, value));
        }

        using var plan = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        var root = plan.RootElement[0];
        var top = root.GetProperty("Plan");
        return new PlanStats(
            root.GetProperty("Execution Time").GetDouble(),
            top.GetProperty("Shared Hit Blocks").GetInt32() + top.GetProperty("Shared Read Blocks").GetInt32(),
            RemovedByFilter(top),
            (long)top.GetProperty("Actual Rows").GetDouble(),
            Nodes(top));

        static string Nodes(JsonElement node)
        {
            var name = node.GetProperty("Node Type").GetString()!;
            if (node.TryGetProperty("Index Name", out var index))
            {
                name += $" ({index.GetString()})";
            }

            return node.TryGetProperty("Plans", out var children)
                ? name + " > " + string.Join(" + ", children.EnumerateArray().Select(Nodes))
                : name;
        }

        static long RemovedByFilter(JsonElement node)
        {
            var removed = node.TryGetProperty("Rows Removed by Filter", out var own) ? (long)own.GetDouble() : 0;
            if (node.TryGetProperty("Plans", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    removed += RemovedByFilter(child);
                }
            }

            return removed;
        }
    }

    /// <summary>One EXPLAIN (ANALYZE, BUFFERS) of a claim's candidate query.</summary>
    private sealed record PlanStats(double ExecutionMs, int Buffers, long RemovedByFilter, long Rows, string Nodes)
    {
        public static PlanStats Median(List<PlanStats> runs)
        {
            static T Mid<T>(IEnumerable<T> values) => values.Order().ElementAt(values.Count() / 2);
            return new PlanStats(
                Mid(runs.Select(r => r.ExecutionMs)), Mid(runs.Select(r => r.Buffers)),
                Mid(runs.Select(r => r.RemovedByFilter)), Mid(runs.Select(r => r.Rows)), runs[0].Nodes);
        }
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

    private static async Task<CapturedCommand> InboxQueryAsync(StoreDatabase db, InboxClaimExclusions excluded, int expectedClaims = 0)
    {
        var capture = new CandidateQueryCapture();
        await using var context = NewContext(db.ConnectionString, capture);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        capture.Arm();
        var claim = await store.ClaimBatchAsync(100, TimeSpan.FromMinutes(5), excluded);
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

    /// <summary>The held pair (the backlog's consumer, tenant A) first, then <paramref name="count"/> - 1 others.</summary>
    private static InboxClaimExclusions Pairs(int count)
        => new([], [.. Enumerable.Range(0, count).Select(i => i == 0 ? (PlanConsumer, "A") : (PlanConsumer, $"T{i}"))]);

    private static InboxMessage InboxRow(int order, InboxMessageStatus status, bool deferred = false, string tenant = "A") => new()
    {
        MessageId = MessageId.New(),
        ConsumerType = PlanConsumer,
        TenantId = tenant,
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
