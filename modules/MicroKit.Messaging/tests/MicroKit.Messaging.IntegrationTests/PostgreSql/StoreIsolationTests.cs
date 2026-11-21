using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Xunit.Abstractions;

using MicroKit.Messaging.IntegrationTests.SecondModule;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// V5 (#188 D9): with the database of one of two stores stopped — its own container — the other
/// store keeps draining at its normal interval; the stopped one backs off, alone, and recovers after
/// a restart.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Normal interval" is measured, not assumed.</b> Store A is published to at a steady rate,
/// faster than its polling interval, so its loop always finds a partial batch and stays at
/// <see cref="Polling"/>. Each A message must be handled within <see cref="Bound"/> of its commit
/// — the measured baseline's P99 times <see cref="BoundFactor"/> — before the outage (the baseline)
/// and during it.
/// </para>
/// <para>
/// A loop shared by the two stores fails the bound: each of A's cycles waits for one of B's, and
/// B's cycles fail on its connect timeout. One back-off state for both fails B's back-off
/// assertion instead: A's successes reset the shared delay, so B never backs off.
/// </para>
/// <para>
/// <b>A missed bound explains itself.</b> Both contexts carry a <see cref="StoreTimeline"/>
/// interceptor and the thread pool is sampled throughout: the failure message gives A's per-message
/// latencies, the configured bound, B's loop timeline (each back-off delay, each strategy retry)
/// and, for each late message, everything B, A and the pool did over its commit-to-handled window.
/// The same report is written to the test output on every run.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class StoreIsolationTests(
    PostgreSqlFixture fixture, StoppablePostgreSqlFixture stoppable, ITestOutputHelper output)
    : IClassFixture<StoppablePostgreSqlFixture>
{
    private static readonly TimeSpan Polling = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxPolling = TimeSpan.FromMilliseconds(1600);

    // The bound, derived from a measured baseline (#188 V5, 2026-11-21): 60 runs of this test,
    // alternating 66b4293 and 4282cb7 on one machine, 0 failures. A's commit-to-handled latency
    // with both stores up, 819 messages pooled: P50 70 ms, P99 183 ms, max 195 ms (the slowest are
    // each run's first two messages: connection and JIT warm-up). With B stopped, 2400 messages:
    // P99 109–113 ms, max 121 ms.
    // Bound = P99 x 3 = 549 ms. The factor leaves room for a slower CI runner, and the bound stays
    // near half of what a shared loop adds to A: B's 1 s connect timeout per cycle (a gate shared
    // by both stores' cycles measured 722–1100 ms on 12 of A's 40 outage messages).
    private static readonly TimeSpan BaselineP99 = TimeSpan.FromMilliseconds(183);
    private const int BoundFactor = 3;
    private static readonly TimeSpan Bound = BaselineP99 * BoundFactor;
    private const string BoundDerivation = "baseline P99 (183ms, 819 samples, 60 runs) x 3";
    private static readonly MessageStoreKey StoreB = EntityFrameworkOutbox.KeyOf<SecondMessagingDbContext>();

    [DockerRequiredFact]
    public async Task WithOneStoresDatabaseStopped_TheOtherKeepsItsCadence_AndTheStoppedOneBacksOffAndRecovers()
    {
        await using var a = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await using var b = await StoreDatabase.CreateAsync<SecondMessagingDbContext>(stoppable.ConnectionString, o => new(o));
        var logs = new CapturingLoggerProvider();
        var handledA = new ConcurrentDictionary<Guid, DateTimeOffset>();
        var recorderB = new SecondModuleRecorder();
        var timeline = new StoreTimeline();
        using var sampling = new CancellationTokenSource();
        var sampler = timeline.SamplePoolAsync(TimeSpan.FromMilliseconds(10), sampling.Token);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Information);
        builder.Services.AddSingleton(handledA);
        builder.Services.AddSingleton(recorderB);
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o
            .UseNpgsql(a.ConnectionString)
            .AddInterceptors(timeline.For("A")));
        // A short connect timeout, so each of B's failed cycles is reported within the outage: with
        // Npgsql's default of 15 s one attempt would span it. A's isolation does not depend on it.
        builder.Services.AddDbContext<SecondMessagingDbContext>(o => o.UseNpgsql(
                new Npgsql.NpgsqlConnectionStringBuilder(b.ConnectionString) { Timeout = 1, CommandTimeout = 2 }.ConnectionString)
            .AddInterceptors(timeline.For("B")));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.store-isolation";
            bus.Publishes<StoreAEvent>();
            bus.Publishes<SecondModuleEvent>();
            bus.AddHandler<StoreAHandler>();
            bus.AddHandler<SecondModuleHandler>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>(s => s.ForAssemblyContaining<StoreIsolationTests>());
            bus.UseEntityFrameworkOutbox<SecondMessagingDbContext>(s => s.ForAssemblyContaining<SecondModuleMarker>());
            bus.UseInMemoryTransport();
            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = Polling;
                o.Outbox.MaxPollingInterval = MaxPolling;
                o.Inbox.PollingInterval = Polling;
                o.Inbox.MaxPollingInterval = MaxPolling;
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
            });
        });
        using var host = builder.Build();
        await host.StartAsync();

        // A is published to from start to finish, so its loop never idles into its own back-off:
        // a pause in A's traffic — while B's container stops, say — would grow A's delay toward
        // MaxPolling and be measured as lateness that B never caused.
        await using var steady = new SteadyPublisher(host);
        try
        {
            // 1. Baseline, both databases up.
            steady.Start();
            await steady.UntilAsync("baseline", count: 10);
            await PublishToBAsync(host, recorderB, count: 2, delay: null);
            await Eventually(() => recorderB.Handled.Count == 2, "store B drains while its database is up");

            // 2. A backlog for B, due only once its database is down, then the outage. A's
            // publications while the container stops count as the outage's.
            var backlog = await PublishToBAsync(host, recorderB, count: 3, delay: TimeSpan.FromSeconds(3));
            var outageMark = logs.Mark();
            steady.Phase = "outage";
            await stoppable.StopAsync();
            recorderB.Handled.Select(h => h.Id).ShouldNotContain(id => backlog.Contains(id), "the backlog waits for the outage");

            // 3. During the outage, A keeps its cadence.
            await steady.UntilAsync("outage", count: 40);
            await steady.StopAsync();
            await Eventually(() => steady.Committed.Keys.All(handledA.ContainsKey), "every A message is handled");
            AssertWithinBound("baseline", steady.Samples("baseline", handledA), timeline, logs, extra: null);
            AssertWithinBound("outage", steady.Samples("outage", handledA), timeline, logs,
                extra: StoreIsolationReport.LoopTimeline(logs.Since(outageMark), StoreB.Value));

            // B backs off on its own curve, growing toward MaxPolling. A's 40 messages bound the
            // window by A's cadence, not by B's cycles, which take longer under load (each waits out
            // its connect timeout): wait, B still down, for the cycles the curve needs.
            List<TimeSpan> BDelays() => [.. logs.Since(outageMark)
                .Where(e => e.EventId == 1401 && Value(e, "Loop") == "outbox" && Value(e, "Store") == StoreB.Value)
                .Select(e => (TimeSpan)e.State.First(kv => kv.Key == "Delay").Value!)];
            // Bounded, and not an assertion: the one below names what B did report.
            var cyclesDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (BDelays().Count < 3 && DateTime.UtcNow < cyclesDeadline)
            {
                await Task.Delay(50);
            }

            var delays = BDelays();
            delays.Count.ShouldBeGreaterThanOrEqualTo(3,
                $"B's outbox loop reports each failed cycle: {string.Join(", ", delays)}; logs: " +
                string.Join(" | ", logs.Since(outageMark).Where(e => e.Level >= LogLevel.Warning)
                    .Select(e => $"{e.EventId} {e.RenderedMessage} {e.ExceptionTypeName}")));
            delays.ShouldBe(delays.Order().ToList(), "B's delay never shrinks while its database is down");
            delays[^1].ShouldBeGreaterThan(delays[0], "B's delay grows");
            delays.ShouldAllBe(d => d <= MaxPolling);
            logs.Since(outageMark)
                .ShouldNotContain(e => e.EventId == 1401 && Value(e, "Store") != StoreB.Value, "store A never failed");

            // 4. B recovers after the restart, and its backlog is delivered.
            await stoppable.StartAsync();
            await Eventually(
                () => backlog.All(id => recorderB.Handled.Any(h => h.Id == id)),
                "store B drains its backlog once its database is back",
                TimeSpan.FromSeconds(20));
        }
        finally
        {
            await steady.StopAsync();
            await host.StopAsync();
            await sampling.CancelAsync();
            await sampler;
            output.WriteLine(StoreIsolationReport.LoopTimeline(logs.Entries, StoreB.Value));
        }
    }

    /// <summary>
    /// Every A message of the phase was handled within <see cref="Bound"/> of its commit; the
    /// phase's report goes to the test output either way, and into the failure message.
    /// </summary>
    private void AssertWithinBound(
        string phase,
        IReadOnlyList<(Guid Id, DateTimeOffset Committed, DateTimeOffset Handled)> samples,
        StoreTimeline timeline,
        CapturingLoggerProvider logs,
        string? extra)
    {
        var report = StoreIsolationReport.Phase(
            phase, samples, Bound, BoundDerivation, timeline, logs.Entries, StoreB.Value) + extra;
        output.WriteLine(report);
        samples.ShouldAllBe(s => s.Handled - s.Committed <= Bound, report);
    }

    /// <summary>Publishes to A every 50 ms, in the background, recording each commit under its phase.</summary>
    private sealed class SteadyPublisher(IHost host) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task? _loop;

        /// <summary>Gets each A message's commit time and the phase it was committed in.</summary>
        public ConcurrentDictionary<Guid, (DateTimeOffset At, string Phase)> Committed { get; } = new();

        /// <summary>Gets or sets the phase the next commits are recorded under.</summary>
        public string Phase { get; set; } = "baseline";

        public void Start() => _loop = RunAsync();

        public async Task StopAsync()
        {
            if (_loop is null)
            {
                return;
            }

            await _stop.CancelAsync();
            await _loop;
            _loop = null;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _stop.Dispose();
        }

        public Task UntilAsync(string phase, int count)
            => Eventually(() => Committed.Values.Count(c => c.Phase == phase) >= count, $"{count} A messages in {phase}");

        public List<(Guid Id, DateTimeOffset Committed, DateTimeOffset Handled)> Samples(
            string phase, ConcurrentDictionary<Guid, DateTimeOffset> handled)
            => [.. Committed.Where(c => c.Value.Phase == phase)
                .OrderBy(c => c.Value.At)
                .Select(c => (c.Key, c.Value.At, handled[c.Key]))];

        private async Task RunAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                var id = Guid.NewGuid();
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new StoreAEvent(id));
                    await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
                }

                Committed[id] = (DateTimeOffset.UtcNow, Phase);
                try
                {
                    await Task.Delay(50, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<HashSet<Guid>> PublishToBAsync(
        IHost host, SecondModuleRecorder recorder, int count, TimeSpan? delay)
    {
        var ids = new HashSet<Guid>();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SecondMessagingDbContext>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new SecondModuleEvent(id));
        }

        if (delay is { } due)
        {
            foreach (var row in context.ChangeTracker.Entries<OutboxMessage>())
            {
                row.Entity.NextRetryAtUtc = DateTimeOffset.UtcNow + due;
            }
        }

        await context.SaveChangesAsync();
        return ids;
    }

    private static async Task Eventually(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, because);
            await Task.Delay(20);
        }
    }

    private static string? Value(CapturedLogEntry entry, string key)
        => entry.State.FirstOrDefault(kv => kv.Key == key).Value?.ToString();
}

[MessageContract("tests.store-isolation.a-event")]
public sealed record StoreAEvent(Guid Id) : IIntegrationEvent;

public sealed class StoreAHandler(ConcurrentDictionary<Guid, DateTimeOffset> handled) : IMessageHandler<StoreAEvent>
{
    public ValueTask HandleAsync(StoreAEvent message, MessageContext context, CancellationToken ct = default)
    {
        handled.TryAdd(message.Id, DateTimeOffset.UtcNow);
        return ValueTask.CompletedTask;
    }
}
