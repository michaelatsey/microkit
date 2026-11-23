using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

/// <summary>
/// Isolation and fairness between the instances of a partitioned store (#191 D6, D7), measured: a row's
/// latency is its commit to its transport send, on <see cref="Stopwatch"/>.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class PartitionedStoreLatencyTests(InstanceServerFixture server, ITestOutputHelper output)
    : IClassFixture<InstanceServerFixture>
{
    // V4, measured 2026-11-22 on one machine, five runs, 40 rows per phase: baseline P99 182–200 ms
    // (P50 74–159 ms); with the fifth instance's database refusing connections, P99 174–207 ms. Bound =
    // the worst baseline P99 (200 ms) x 3 = 600 ms: the factor leaves room for a slower runner, as #188's V5.
    private static readonly TimeSpan V4Bound = TimeSpan.FromMilliseconds(600);

    // V5, measured the same day, five runs, 20 light rows each, all sent while both backlogs were still
    // draining: max 1106–1225 ms (P50 824–984 ms) — two heavy batches ahead at most, plus the light loop's
    // own polling. Bound = the worst max (1225 ms) x 3 ≈ 3700 ms. An unfair gate makes the light row wait for
    // the backlogs, about 20,000 sends of 1 ms or more: tens of seconds.
    private static readonly TimeSpan V5Bound = TimeSpan.FromMilliseconds(3700);

    /// <summary>
    /// V4: with one instance's database refusing connections, the other instances' drain latency stays
    /// within a measured bound, and the stopped instance backs off on its own loops.
    /// </summary>
    [DockerRequiredFact]
    public async Task WithOneInstancesDatabaseStopped_TheOthersKeepTheirLatency_AndItBacksOffAlone()
    {
        await using var host = PartitionedHost.Build();
        host.Output = output;
        var ids = await host.ProvisionAsync(server, 5);
        var healthy = ids[..4];
        var stopped = ids[4];
        host.Declare(ids);
        await host.StartAsync();

        var baseline = await MeasureAsync(host, healthy, count: 40);
        await server.StopDatabaseAsync(host.Catalog.ConnectionStringOf(stopped));
        var mark = host.Logs.Entries.Count;
        var outage = await MeasureAsync(host, healthy, count: 40);

        output.WriteLine($"V4 baseline: {Describe(baseline)}");
        output.WriteLine($"V4 outage:   {Describe(outage)}");
        output.WriteLine($"V4 bound:    {V4Bound.TotalMilliseconds} ms");

        await Eventually.HoldsAsync(
            () => host.Logs.Entries.Skip(mark).Count(e => e.EventId == 1412) >= 3,
            "the stopped instance's loops fail and back off");
        var failures = host.Logs.Entries.Skip(mark).Where(e => e.EventId == 1412).ToList();
        failures.ShouldAllBe(
            e => e.Message.Contains($"instance '{stopped}'", StringComparison.Ordinal),
            "only the stopped instance's loops fail, each failure naming it (1412: {Store} and {Instance})");

        Percentile(outage, 0.99).ShouldBeLessThanOrEqualTo(V4Bound, $"outage {Describe(outage)} against baseline {Describe(baseline)}");
    }

    /// <summary>
    /// V5: with the gate at one batch, two instances holding 10,000 pending rows each do not delay a row of
    /// a third instance beyond a measured bound: the gate admits in arrival order.
    /// </summary>
    [DockerRequiredFact]
    public async Task UnderAGateOfOne_TwoLargeBacklogs_DoNotDelayAnotherInstancesRow()
    {
        const int Backlog = 10_000;
        await using var host = PartitionedHost.Build(options: o => o.Instances.MaxConcurrentBatches = 1);
        host.Output = output;
        var heavy = await host.ProvisionAsync(server, 2, prefix: "h");
        await host.ProvisionAsync(server, 1, prefix: "b");
        foreach (var id in heavy)
        {
            await host.PublishTransportAsync(id);
            await CloneOutboxRowAsync(host.Catalog.ConnectionStringOf(id), Backlog - 1);
        }

        var heavyDatabases = heavy.Select(host.Catalog.DatabaseOfInstance).ToHashSet(StringComparer.Ordinal);

        // Each heavy send costs a millisecond, so a heavy batch holds the gate for about 100 ms and the two
        // backlogs for tens of seconds: what an unfair gate would make the light row wait is then far
        // above the bound, not within its margin.
        foreach (var database in heavyDatabases)
        {
            host.Recorder.SendCost[database] = TimeSpan.FromMilliseconds(1);
        }
        host.Declare([.. heavy, "b0"]);
        await host.StartAsync();

        var samples = new List<TimeSpan>();
        var contended = 0;
        while (samples.Count < 20)
        {
            var heavyPending = 2 * Backlog - host.Recorder.Sent.Count(s => heavyDatabases.Contains(s.Database));
            if (heavyPending <= 0)
            {
                break;
            }

            var (latency, stillPending) = await OneAsync(host, "b0", heavyDatabases, 2 * Backlog);
            samples.Add(latency);
            if (stillPending > 0)
            {
                contended++;
            }
        }

        output.WriteLine($"V5 light instance, {samples.Count} rows, {contended} sent while the backlogs were still draining: {Describe(samples)}");
        output.WriteLine($"V5 bound: {V5Bound.TotalMilliseconds} ms");
        samples.Max().ShouldBeLessThanOrEqualTo(V5Bound, Describe(samples));
        contended.ShouldBeGreaterThanOrEqualTo(10, "control: the light rows were measured while the large backlogs held the gate");
    }

    private static async Task<List<TimeSpan>> MeasureAsync(PartitionedHost host, string[] instances, int count)
    {
        var latencies = new List<TimeSpan>(count);
        for (var i = 0; i < count; i++)
        {
            var (latency, _) = await OneAsync(host, instances[i % instances.Length], [], 0);
            latencies.Add(latency);
            await Task.Delay(20);
        }

        return latencies;
    }

    /// <summary>Publishes one row and waits for its send; returns its latency and the heavy rows not yet sent then.</summary>
    private static async Task<(TimeSpan Latency, int HeavyPending)> OneAsync(
        PartitionedHost host, string instance, HashSet<string> heavy, int heavyTotal)
    {
        var message = await host.PublishTransportAsync(instance);
        var committed = Stopwatch.GetTimestamp();
        (Guid MessageId, string Database, long At) sent = default;
        await Eventually.HoldsAsync(
            () =>
            {
                foreach (var s in host.Recorder.Sent)
                {
                    if (s.MessageId == message)
                    {
                        sent = s;
                        return true;
                    }
                }

                return false;
            },
            $"row {message} of {instance} is sent",
            TimeSpan.FromSeconds(120));
        var pending = heavy.Count == 0 ? 0 : heavyTotal - host.Recorder.Sent.Count(s => heavy.Contains(s.Database));
        return (Stopwatch.GetElapsedTime(committed, sent.At), pending);
    }

    /// <summary>Copies the database's one outbox row <paramref name="copies"/> times, each with its own id.</summary>
    private static async Task CloneOutboxRowAsync(string connectionString, int copies)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var columns = new List<string>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = 'OutboxMessages' ORDER BY ordinal_position";
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        var list = string.Join(", ", columns.Select(c => $"\"{c}\""));
        var select = string.Join(", ", columns.Select(c => c == "Id" ? "gen_random_uuid()" : $"o.\"{c}\""));
        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO \"OutboxMessages\" ({list}) SELECT {select} FROM \"OutboxMessages\" o, generate_series(1, {copies})";
        (await insert.ExecuteNonQueryAsync()).ShouldBe(copies);
    }

    private static TimeSpan Percentile(List<TimeSpan> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static string Describe(List<TimeSpan> values)
        => values.Count == 0
            ? "no sample"
            : $"n={values.Count} p50={Percentile(values, 0.5).TotalMilliseconds:F0}ms p99={Percentile(values, 0.99).TotalMilliseconds:F0}ms max={values.Max().TotalMilliseconds:F0}ms";
}
