using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
/// <see cref="Polling"/>. Each A message must be handled within <see cref="Polling"/> plus
/// <see cref="Margin"/> of its commit, before the outage (the baseline) and during it.
/// </para>
/// <para>
/// A loop shared by the two stores, or one back-off state for both, fails this: B's failures grow
/// the shared delay toward <see cref="MaxPolling"/>, far past the bound.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class StoreIsolationTests(PostgreSqlFixture fixture, StoppablePostgreSqlFixture stoppable)
    : IClassFixture<StoppablePostgreSqlFixture>
{
    private static readonly TimeSpan Polling = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxPolling = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(400);
    private static readonly MessageStoreKey StoreB = EntityFrameworkOutbox.KeyOf<SecondMessagingDbContext>();

    [DockerRequiredFact]
    public async Task WithOneStoresDatabaseStopped_TheOtherKeepsItsCadence_AndTheStoppedOneBacksOffAndRecovers()
    {
        await using var a = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await using var b = await StoreDatabase.CreateAsync<SecondMessagingDbContext>(stoppable.ConnectionString, o => new(o));
        var logs = new CapturingLoggerProvider();
        var handledA = new ConcurrentDictionary<Guid, DateTimeOffset>();
        var recorderB = new SecondModuleRecorder();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Information);
        builder.Services.AddSingleton(handledA);
        builder.Services.AddSingleton(recorderB);
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(a.ConnectionString));
        // A short connect timeout, so each of B's failed cycles is reported within the outage: with
        // Npgsql's default of 15 s one attempt would span it. A's isolation does not depend on it.
        builder.Services.AddDbContext<SecondMessagingDbContext>(o => o.UseNpgsql(
            new Npgsql.NpgsqlConnectionStringBuilder(b.ConnectionString) { Timeout = 1, CommandTimeout = 2 }.ConnectionString));
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

        try
        {
            // 1. Baseline, both databases up.
            var baseline = await PublishToAAtASteadyRateAsync(host, handledA, count: 10);
            await PublishToBAsync(host, recorderB, count: 2, delay: null);
            await Eventually(() => recorderB.Handled.Count == 2, "store B drains while its database is up");
            baseline.ShouldAllBe(l => l <= Polling + Margin, $"baseline latencies: {Show(baseline)}");

            // 2. A backlog for B, due only once its database is down, then the outage.
            var backlog = await PublishToBAsync(host, recorderB, count: 3, delay: TimeSpan.FromSeconds(3));
            var outageMark = logs.Mark();
            await stoppable.StopAsync();
            recorderB.Handled.Select(h => h.Id).ShouldNotContain(id => backlog.Contains(id), "the backlog waits for the outage");

            // 3. During the outage, A keeps its cadence.
            var during = await PublishToAAtASteadyRateAsync(host, handledA, count: 40);
            during.ShouldAllBe(l => l <= Polling + Margin, $"latencies while B is down: {Show(during)}");

            // B backs off on its own curve, growing toward MaxPolling.
            var delays = logs.Since(outageMark)
                .Where(e => e.EventId == 1401 && Value(e, "Loop") == "outbox" && Value(e, "Store") == StoreB.Value)
                .Select(e => (TimeSpan)e.State.First(kv => kv.Key == "Delay").Value!)
                .ToList();
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
            await host.StopAsync();
        }
    }

    private static async Task<List<TimeSpan>> PublishToAAtASteadyRateAsync(
        IHost host, ConcurrentDictionary<Guid, DateTimeOffset> handled, int count)
    {
        var committed = new Dictionary<Guid, DateTimeOffset>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            await using (var scope = host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new StoreAEvent(id));
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
            }

            committed[id] = DateTimeOffset.UtcNow;
            await Task.Delay(50);
        }

        await Eventually(() => committed.Keys.All(handled.ContainsKey), "every A message is handled");
        return [.. committed.Select(c => handled[c.Key] - c.Value)];
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

    private static string Show(IEnumerable<TimeSpan> latencies)
        => string.Join(", ", latencies.Select(l => $"{l.TotalMilliseconds:F0}ms"));
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
