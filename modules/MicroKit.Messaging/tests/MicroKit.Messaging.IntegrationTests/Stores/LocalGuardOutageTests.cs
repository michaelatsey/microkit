using System.Collections.Concurrent;
using MicroKit.Messaging.IntegrationTests.SecondModule;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// A local row whose consumer is guarded by another store, that store down (#188): the guard's store
/// cannot be read, which is a dependency outage — the row is deferred under the local endpoint's
/// back-off, never charged an attempt and never dead-lettered, while the dispatching store's
/// transport rows keep their cadence.
/// </summary>
public sealed class LocalGuardOutageTests
{
    [Fact]
    public Task ALocalRowWhoseConsumersStoreIsDown_IsDeferred_NotDeadLettered_AndTheStoresOtherRowsKeepTheirCadence()
        => Task.Run(async () =>
        {
            using var connectionOfA = new SqliteConnection("Data Source=:memory:");
            connectionOfA.Open();

            // Store B's database cannot be opened: a directory that does not exist, read-write only.
            var unreachable = Path.Combine(Path.GetTempPath(), $"mk-188-{Guid.NewGuid():N}", "missing", "b.db");
            var transport = new SendingTransport();
            var logs = new CapturingLoggerProvider();
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(logs));
            services.AddSingleton<IMessageTransport>(transport);
            services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connectionOfA));
            services.AddDbContext<SecondMessagingDbContext>(o => o.UseSqlite($"Data Source={unreachable};Mode=ReadWrite"));
            services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = "tests.guard-outage";
                bus.Publishes<OutageEvent>();
                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>(store => store.ForAssemblyContaining<LocalGuardOutageTests>());
                bus.UseEntityFrameworkOutbox<SecondMessagingDbContext>(store => store.ForAssemblyContaining<SecondModuleMarker>());
                bus.AddLocalConsumerSource(new ConsumerInStoreB());
                bus.Configure(o =>
                {
                    o.Outbox.MaxRetries = 2;   // a transient verdict would dead-letter within two passes
                    o.Outbox.RetentionDays = 0;
                    o.Inbox.RetentionDays = 0;
                });
            });

            await using var provider = services.BuildServiceProvider();
            await using (var schema = provider.CreateAsyncScope())
            {
                await schema.ServiceProvider.GetRequiredService<TestMessagingDbContext>().Database.EnsureCreatedAsync();
            }

            var published = new OutageEvent(Guid.NewGuid());
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ILocalMessageSender>()
                    .SendAsync(new OutageNotice(), MessageId.New(), DateTimeOffset.UtcNow);
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(published);
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
            }

            var storeA = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();
            async Task<OutboxBatchResult> PassAsync()
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(storeA).ProcessBatchAsync(10);
            }

            var first = await PassAsync();

            // The transport row goes out in the very first pass, whatever the local endpoint does.
            transport.Sent.ShouldContain(published.Id, "the dispatching store's transport row keeps its cadence");
            first.Published.ShouldBe(1);

            // As many passes again as would dead-letter a transient verdict: the deferred row is not
            // claimed before its back-off ends, and nothing is charged to it.
            for (var pass = 0; pass < 3; pass++)
            {
                (await PassAsync()).Claimed.ShouldBe(0, "the deferred local row waits out the local endpoint's back-off");
            }

            await using var read = provider.CreateAsyncScope();
            var local = await read.ServiceProvider.GetRequiredService<TestMessagingDbContext>()
                .Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Endpoint == OutboxMessage.LocalEndpoint);
            local.DeadLettered.ShouldBeFalse("a dependency outage never dead-letters");
            local.Status.ShouldBe(OutboxMessageStatus.Pending);
            local.RetryCount.ShouldBe(0, "no attempt is charged");
            local.NextRetryAtUtc.ShouldNotBeNull("deferred to the end of the local endpoint's back-off");
            local.NextRetryAtUtc!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

            // Unreachable for any reason — a wrong password included — defers and never dead-letters;
            // the Error carries the store's own reason, so a configuration error is visible.
            var outage = logs.Entries.First(e => e.EventId == 2209);
            outage.Level.ShouldBe(LogLevel.Error);
            outage.ExceptionTypeName.ShouldBe(nameof(InboxDependencyUnavailableException));
            outage.ExceptionMessage.ShouldNotBeNull().ShouldContain("unable to open database file", Case.Insensitive);
        });

    private sealed record OutageNotice;

    [MessageContract("tests.guard-outage.event")]
    private sealed record OutageEvent(Guid Id) : IIntegrationEvent;

    /// <summary>A local consumer whose handler type lives in the assembly bound to store B.</summary>
    private sealed class ConsumerInStoreB : ILocalConsumerSource
    {
        public IEnumerable<LocalConsumer> GetConsumers() =>
            [new LocalConsumer(typeof(OutageNotice), "tests.guard-outage.consumer", typeof(SecondModuleMarker), (_, _, _, _) => ValueTask.CompletedTask)];
    }

    private sealed class SendingTransport : IMessageTransport
    {
        public ConcurrentBag<Guid> Sent { get; } = [];

        public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            Sent.Add(System.Text.Json.JsonSerializer.Deserialize<OutageEvent>(envelope.Body.Span, System.Text.Json.JsonSerializerOptions.Web)!.Id);
            return ValueTask.CompletedTask;
        }
    }
}
