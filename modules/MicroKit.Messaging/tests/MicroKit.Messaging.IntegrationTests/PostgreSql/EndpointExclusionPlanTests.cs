using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The claim that leaves a down endpoint out reaches the other endpoint's rows without walking the
/// down endpoint's backlog or the published history (#188): measured on PostgreSQL, both directions,
/// on the claim's own SQL, by the buffers it touches.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EndpointExclusionPlanTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const int Backlog = 20_000;
    private const int History = 20_000;
    private const int Wanted = 3;

    /// <summary>The buffers one claim may touch: a handful of index and heap pages, not the backlog's.</summary>
    private const int BufferBudget = 60;

    [DockerRequiredFact]
    public Task AClaimExcludingTheTransport_ReadsTheLocalRows_NotTheBacklogNorTheHistory()
        => ClaimExcludingAsync(OutboxEndpoints.Transport);

    [DockerRequiredFact]
    public Task AClaimExcludingTheLocalEndpoint_ReadsTheTransportRows_NotTheBacklogNorTheHistory()
        => ClaimExcludingAsync(OutboxEndpoints.Local);

    private async Task ClaimExcludingAsync(OutboxEndpoints excluded)
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var down = excluded is OutboxEndpoints.Transport ? null : OutboxMessage.LocalEndpoint;
        var up = excluded is OutboxEndpoints.Transport ? OutboxMessage.LocalEndpoint : null;
        await SeedAsync(db.ConnectionString, down, up);

        var capture = new CandidateQueryCapture();
        await using var context = new TestMessagingDbContext(new DbContextOptionsBuilder<TestMessagingDbContext>()
            .UseNpgsql(db.ConnectionString)
            .AddInterceptors(capture)
            .Options);
        var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        var query = capture.Arm();
        var claim = await store.ClaimBatchAsync(100, TimeSpan.FromMinutes(5), excluded);
        claim.Messages.ShouldAllBe(m => m.Endpoint == up, "only the other endpoint's rows are claimed");
        claim.Count.ShouldBe(Wanted);

        // Explained on the rows as the claim found them: release the claim first.
        await store.ApplyOutcomesAsync(claim.Token, [.. claim.Messages.Select(m => OutboxOutcome.Released(m.Id))]);
        var plan = await ExplainAsync(db.ConnectionString, query.Candidates.ShouldNotBeNull());
        output.WriteLine(string.Join(Environment.NewLine, plan));

        SharedBuffers(plan).ShouldBeLessThan(BufferBudget,
            $"{Backlog} rows of the down endpoint and {History} published rows are not walked: " + string.Join(" | ", plan));
    }

    /// <summary>
    /// The shared buffers — hit and read — of the plan's top node, which include every node below it.
    /// </summary>
    private static int SharedBuffers(List<string> plan)
    {
        using var json = System.Text.Json.JsonDocument.Parse(string.Concat(plan));
        var top = json.RootElement[0].GetProperty("Plan");
        return top.GetProperty("Shared Hit Blocks").GetInt32() + top.GetProperty("Shared Read Blocks").GetInt32();
    }

    private static async Task SeedAsync(string connectionString, string? down, string? up)
    {
        await using var context = new TestMessagingDbContext(
            new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(connectionString).Options);
        OutboxMessage Row(string? endpoint, int order, OutboxMessageStatus status) => new()
        {
            Id = MessageId.New(),
            Endpoint = endpoint,
            ContractName = endpoint is null ? "tests.plan.v1" : null,
            Source = endpoint is null ? "tests.plan" : null,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = status,
            ProcessedAtUtc = status is OutboxMessageStatus.Published ? Now : null,
            OccurredOnUtc = Now,
            CreatedAtUtc = Now.AddMilliseconds(order - ((Backlog + History) * 2)),
            CorrelationId = CorrelationId.New(),
        };

        // The published history of the endpoint that is up, oldest; then the down endpoint's backlog,
        // heading the queue; then the few rows the claim must reach.
        context.OutboxMessages.AddRange(Enumerable.Range(0, History).Select(i => Row(up, i, OutboxMessageStatus.Published)));
        context.OutboxMessages.AddRange(Enumerable.Range(0, Backlog).Select(i => Row(down, History + i, OutboxMessageStatus.Pending)));
        context.OutboxMessages.AddRange(Enumerable.Range(0, Wanted).Select(i => Row(up, History + Backlog + i, OutboxMessageStatus.Pending)));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("ANALYZE \"OutboxMessages\"");
    }

    private static async Task<List<string>> ExplainAsync(string connectionString, CapturedCommand query)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query.Text;
        foreach (var (name, value) in query.Parameters)
        {
            command.Parameters.Add(new Npgsql.NpgsqlParameter(name, value));
        }

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }

    private sealed record CapturedCommand(string Text, List<(string Name, object? Value)> Parameters);

    /// <summary>Captures the claim's candidate query — the SELECT the claim orders and limits.</summary>
    private sealed class CandidateQueryCapture : DbCommandInterceptor
    {
        private bool _armed;

        public CapturedCommand? Candidates { get; private set; }

        public CandidateQueryCapture Arm()
        {
            _armed = true;
            return this;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && Candidates is null && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                Candidates = new CapturedCommand(
                    command.CommandText,
                    [.. command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, (object?)p.Value))]);
            }

            return ValueTask.FromResult(result);
        }
    }
}
