using MicroKit.Execution.Abstractions;
using MicroKit.Messaging.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// A broker outage longer than a batch never starves the store's local rows (#188), over the real
/// claim: the transport rows the outage reaches are deferred to the end of the endpoint's back-off,
/// so the next claim skips them and reaches the local rows behind them, and the broker is tried once
/// per back-off period rather than once per cycle.
/// </summary>
public sealed class EndpointOutageClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const int BatchSize = 3;

    [Fact]
    public async Task ABrokerOutageLongerThanABatch_NeverStarvesTheLocalRows_AndTriesTheBrokerOncePerBackOff()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var clock = new FakeTimeProvider(Now);
        var options = new OutboxProcessorOptions { BatchSize = BatchSize };

        // Twice a batch of transport rows, staged first, so they head every claim by CreatedAtUtc.
        var transportRows = Enumerable.Range(0, BatchSize * 2).Select(i => Row(endpoint: null, i)).ToList();
        var localRows = Enumerable.Range(0, 2).Select(i => Row(OutboxMessage.LocalEndpoint, 100 + i)).ToList();
        await using (var seed = Context(conn))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.OutboxMessages.AddRange([.. transportRows, .. localRows]);
            await seed.SaveChangesAsync();
        }

        var broker = new Dispatcher(_ => new OutboxTransportUnavailableException("broker down"));
        var local = new Dispatcher(_ => null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMicroKitMessaging(_ => { });
        services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Transport, broker);
        services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Local, local);
        await using var provider = services.BuildServiceProvider();
        var backoff = new OutboxEndpointBackoff(options.PollingInterval, options.TransportUnavailableBackoff);

        async Task CycleAsync()
        {
            await using var context = Context(conn);
            var processor = new OutboxProcessor(
                new EfOutboxStore<TestMessagingDbContext>(context, clock),
                provider.GetRequiredService<IExecutionScopeFactory>(),
                options,
                clock,
                Random.Shared,
                NullLogger<OutboxProcessor>.Instance,
                endpointBackoff: backoff);
            await processor.ProcessBatchAsync(BatchSize);
        }

        // Three cycles at the same instant: enough to reach the local rows only if the deferred
        // transport rows stop heading the claim.
        for (var cycle = 0; cycle < 3; cycle++)
        {
            await CycleAsync();
        }

        local.Dispatched.Order().ShouldBe(localRows.Select(r => r.Id).Order(), "the local rows went out behind the outage");
        broker.Dispatched.Count.ShouldBe(1, "a down broker is tried once per back-off period, not once per row or cycle");

        // Only the batch that met the outage was rewritten; the rest of the backlog was left out of
        // every claim, untouched.
        var firstBatch = transportRows.Take(BatchSize).Select(r => r.Id).ToHashSet();
        await using (var read = Context(conn))
        {
            var transport = await read.OutboxMessages.AsNoTracking().Where(m => m.Endpoint == null).ToListAsync();
            transport.ShouldAllBe(m => m.Status == OutboxMessageStatus.Pending && m.RetryCount == 0, "no attempt is charged");
            transport.Where(m => firstBatch.Contains(m.Id)).ShouldAllBe(m => m.NextRetryAtUtc == Now + options.PollingInterval);
            transport.Where(m => !firstBatch.Contains(m.Id)).ShouldAllBe(m => m.NextRetryAtUtc == null, "never claimed while the broker was down");
        }

        // Its back-off over, the broker is tried again — once — and the back-off doubles.
        clock.Advance(options.PollingInterval);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await CycleAsync();
        }

        broker.Dispatched.Count.ShouldBe(2);
        await using (var read = Context(conn))
        {
            var doubled = clock.GetUtcNow() + (options.PollingInterval * 2);
            (await read.OutboxMessages.AsNoTracking().Where(m => m.Endpoint == null).ToListAsync())
                .Where(m => firstBatch.Contains(m.Id))
                .ShouldAllBe(m => m.NextRetryAtUtc == doubled && m.RetryCount == 0);
        }
    }

    private static TestMessagingDbContext Context(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);

    private static OutboxMessage Row(string? endpoint, int order) => new()
    {
        Id = MessageId.New(),
        Endpoint = endpoint,
        ContractName = endpoint is null ? "tests.outage.v1" : null,
        Source = endpoint is null ? "tests.outage" : null,
        TenantId = null,
        EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
        Payload = "{}",
        Status = OutboxMessageStatus.Pending,
        OccurredOnUtc = Now,
        CreatedAtUtc = Now.AddSeconds(order - 1000),
        CorrelationId = CorrelationId.New(),
    };

    private sealed class Dispatcher(Func<OutboxMessage, Exception?> script) : IOutboxDispatcher
    {
        public List<MessageId> Dispatched { get; } = [];

        public ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
        {
            lock (Dispatched)
            {
                Dispatched.Add(message.Id);
            }

            var fault = script(message);
            return fault is null ? ValueTask.CompletedTask : ValueTask.FromException(fault);
        }
    }
}
