using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The module's metrics on a host built as the README shows (#126), under the Development defaults —
/// <c>ValidateOnBuild</c> and <c>ValidateScopes</c> on — with one EF store on PostgreSQL: both meters
/// publish their instruments, and one published row is counted once and sampled once.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class MetricsHostTests(PostgreSqlFixture fixture)
{
    private static readonly string[] NewInstruments =
    [
        "microkit.outbox.messages.settled",
        "microkit.outbox.dispatch.delay",
        "microkit.outbox.endpoints.backoff",
        "microkit.outbox.tenants.backoff",
        "microkit.inbox.tenants.backoff",
    ];

    [DockerRequiredFact]
    public async Task ADevelopmentHostWithAPostgreSqlStore_PublishesItsInstruments_AndCountsAPublishedRowOnce()
    {
        await using var database = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ApplicationName = "tests.metrics-host",
        });
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(database.ConnectionString));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.metrics-host";
            bus.Publishes<MetricsHostEvent>();
            bus.AddHandler<MetricsHostHandler>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            bus.UseInMemoryTransport();
            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Outbox.MaxPollingInterval = TimeSpan.FromMilliseconds(200);
                o.Inbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Inbox.MaxPollingInterval = TimeSpan.FromMilliseconds(200);
            });
        });

        using var host = builder.Build();
        var outbox = host.Services.GetRequiredService<OutboxMetrics>().Meter;
        var inbox = host.Services.GetRequiredService<InboxMetrics>().Meter;
        using var recorder = new Recorder(outbox, inbox);
        await host.StartAsync();
        try
        {
            foreach (var name in NewInstruments)
            {
                recorder.Published.ShouldContain(name);
            }

            recorder.PublishedOn(outbox).Count.ShouldBe(4, "the outbox meter carries the four outbox instruments");
            recorder.PublishedOn(inbox).ShouldContain("microkit.inbox.tenants.backoff");

            await using (var scope = host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new MetricsHostEvent(Guid.NewGuid()));
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
            }

            await Eventually(() => recorder.Of("microkit.outbox.messages.settled").Count > 0, "the row is published and settled");

            var settled = recorder.Of("microkit.outbox.messages.settled").ShouldHaveSingleItem();
            settled.Value.ShouldBe(1);
            settled.Tags["outcome"].ShouldBe("published");
            settled.Tags["endpoint"].ShouldBe("transport");
            settled.Tags["store.key"].ShouldBe(EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>().Value);
            var delay = recorder.Of("microkit.outbox.dispatch.delay").ShouldHaveSingleItem();
            delay.Value.ShouldBeGreaterThanOrEqualTo(0d);
            delay.Tags["endpoint"].ShouldBe("transport");

            // The store's first cycles ran: the back-off counters report it, at zero.
            await Eventually(() => recorder.Observe("microkit.inbox.tenants.backoff").Count == 1, "the inbox loop ran a cycle");
            recorder.Observe("microkit.inbox.tenants.backoff").ShouldHaveSingleItem().Value.ShouldBe(0d);
            recorder.Observe("microkit.outbox.endpoints.backoff").Select(r => r.Value).ShouldBe([0d, 0d]);
            recorder.Observe("microkit.outbox.tenants.backoff").Select(r => r.Value).ShouldBe([0d, 0d]);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            deadline.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), because);
            await Task.Delay(20);
        }
    }

    /// <summary>Records the measurements of the given meter instances only, never every meter of a name.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<Instrument> _published = new();
        private readonly ConcurrentQueue<Recorded> _recorded = new();
        private readonly ConcurrentQueue<Recorded> _observed = new();

        public Recorder(params Meter[] meters)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (Array.IndexOf(meters, instrument.Meter) >= 0)
                {
                    _published.Enqueue(instrument);
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
            _listener.SetMeasurementEventCallback<int>((i, v, tags, _) => Add(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
            _listener.Start();
        }

        public List<string> Published => [.. _published.Select(i => i.Name)];

        public List<string> PublishedOn(Meter meter) => [.. _published.Where(i => i.Meter == meter).Select(i => i.Name)];

        public List<Recorded> Of(string instrument) => [.. _recorded.Where(r => r.Instrument == instrument)];

        /// <summary>Runs the observable callbacks once and returns what <paramref name="instrument"/> observed.</summary>
        public List<Recorded> Observe(string instrument)
        {
            _observed.Clear();
            _listener.RecordObservableInstruments();
            return [.. _observed.Where(r => r.Instrument == instrument)];
        }

        public void Dispose() => _listener.Dispose();

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            (instrument.IsObservable ? _observed : _recorded).Enqueue(new Recorded(instrument.Name, value, copy));
        }

        internal sealed record Recorded(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
    }
}

[MessageContract("tests.metrics-host.event")]
public sealed record MetricsHostEvent(Guid Id) : IIntegrationEvent;

public sealed class MetricsHostHandler : IMessageHandler<MetricsHostEvent>
{
    public ValueTask HandleAsync(MetricsHostEvent message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
