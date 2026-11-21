namespace MicroKit.Messaging.UnitTests.Local;

using System.Collections.Concurrent;
using MicroKit.Messaging.Local;
using MicroKit.Messaging.UnitTests.Bus;
using MicroKit.Messaging.UnitTests.Readme;
using Microsoft.Extensions.Hosting;

/// <summary>
/// The local endpoint of a host with no store (#188 D7): #186's in-process path — one scope per
/// consumer, never the sender's, in-memory retries and dead letters — and no transport involved.
/// </summary>
public sealed class LocalEndpointWithoutStoreTests
{
    [Fact]
    public async Task ALocalMessage_IsDeliveredToEachConsumer_EachInAScopeOfItsOwn_NeverTheSenders()
    {
        await using var host = await LocalHost.StartAsync();
        Guid senderScope;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            senderScope = scope.ServiceProvider.GetRequiredService<ScopeMarker>().Id;
            await scope.ServiceProvider.GetRequiredService<ILocalMessageSender>()
                .SendAsync(new Parcel("p1"), MessageId.New(), DateTimeOffset.UtcNow);
        }

        await Eventually.HoldsAsync(() => host.Log.Calls.Count == 2, "both consumers ran");
        host.Log.Calls.Select(c => c.Consumer).Order(StringComparer.Ordinal).ShouldBe(["first", "second"]);
        host.Log.Calls.Select(c => c.Scope).Distinct().Count().ShouldBe(2, "one scope per consumer");
        host.Log.Calls.ShouldAllBe(c => c.Scope != senderScope, "a consumer never runs in the sender's scope");
        host.Services.GetService<IMessageTransport>().ShouldBeNull("no transport at all");
    }

    [Fact]
    public async Task ATransientFailure_IsRedelivered_AndAPermanentOne_DeadLettersAtOnce()
    {
        await using var host = await LocalHost.StartAsync(fail: (consumer, parcel, call) =>
            parcel switch
            {
                "flaky" when consumer == "first" && call == 1 => new InvalidOperationException("transient"),
                "poison" when consumer == "first" => new PermanentMessageException("never"),
                _ => null,
            });

        await host.SendAsync(new Parcel("flaky"));
        await host.SendAsync(new Parcel("poison"));

        await Eventually.HoldsAsync(() => host.Queue.DeadLettered == 1, "the poison message dead-letters");
        await Eventually.HoldsAsync(
            () => host.Log.Calls.Count(c => c.Parcel == "flaky" && c.Consumer == "first") == 2,
            "the transient failure is redelivered");
        host.Log.Calls.Count(c => c.Parcel == "poison" && c.Consumer == "first").ShouldBe(1, "a permanent verdict is not retried");
        host.Logs.Entries.Count(e => e.Id.Id == DeadLetteredEvent).ShouldBe(1, "each dead letter is logged, with its id and type");
    }

    [Fact]
    public async Task TwoConsumers_OneFailingTransiently_TheOtherRunsExactlyOnce_AndTheFailingOneIsRetriedUntilItSucceeds()
    {
        // One entry per (message, consumer), as a store delivers once per consumer (D8): a
        // consumer's failure redelivers that consumer alone.
        await using var host = await LocalHost.StartAsync(fail: (consumer, parcel, call) =>
            parcel == "p" && consumer == "first" && call <= 3 ? new InvalidOperationException($"transient {call}") : null);

        await host.SendAsync(new Parcel("p"));

        await Eventually.HoldsAsync(
            () => host.Log.Calls.Count(c => c.Consumer == "first") == 4, "the failing consumer is retried until it succeeds");
        await host.SettleAsync();
        host.Log.Calls.Count(c => c.Parcel == "p" && c.Consumer == "first").ShouldBe(4, "three failures, then one success, then nothing");
        host.Log.Calls.Count(c => c.Parcel == "p" && c.Consumer == "second").ShouldBe(1, "a consumer that succeeded never runs again");
        host.Queue.DeadLettered.ShouldBe(0);
    }

    [Fact]
    public async Task AMessageNoConsumerHandles_IsNotQueued_AndIsReported()
    {
        await using var host = await LocalHost.StartAsync();

        await host.SendAsync(new Unconsumed());

        host.Logs.Entries.ShouldContain(
            e => e.Id.Id == 3001 && e.Level == LogLevel.Warning && e.Message.Contains(nameof(Unconsumed)),
            "the verdict of a message no handler consumes: acknowledged, and never silent");
        host.Queue.DeadLettered.ShouldBe(0);
        await host.SettleAsync();
        host.Log.Calls.ShouldAllBe(c => c.Parcel == LocalHost.Sentinel, "nothing was queued for the unconsumed message");
    }

    [Fact]
    public void AConsumerSource_BringsTheQueueWorker_Once()
    {
        var services = new ServiceCollection();
        services.AddMicroKitMessaging(bus => bus
            .AddLocalConsumerSource(new ParcelConsumers((_, _, _) => null))
            .AddLocalConsumerSource(new ParcelConsumers((_, _, _) => null)));

        services.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LocalQueueWorker))
            .ShouldBe(1);
    }

    [Fact]
    public async Task WithTwoStores_ALocalConsumerBoundToNeither_FailsAtStart_NamingItAndTheFix()
    {
        // D12: a local consumer is guarded by the inbox of its store, so with several stores its
        // handler's assembly must be bound to one — as a bus handler's must.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton<CallLog>();
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.local";
            bus.AddMessageStore(new MessageStoreKey("tests:a"), store => store.BindAssembly(typeof(string).Assembly));
            bus.AddMessageStore(new MessageStoreKey("tests:b"), _ => { });
            bus.AddLocalConsumerSource(new ParcelConsumers((_, _, _) => null));
        });
        using var host = builder.Build();

        var fault = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        fault.Message.ShouldContain($"Local consumer '{typeof(ParcelConsumers).FullName}' is bound to no store");
        fault.Message.ShouldContain("ForAssemblyContaining");
    }

    [Fact]
    public async Task ALoggerThatThrowsOnADeadLetter_DoesNotEndTheLoop()
    {
        // Review finding (#188 P3): a disposition log escaping the worker faulted it, and with it
        // every later local message. Settled first, logged through TryLog.
        var throwing = new ThrowingLoggerProvider(e => e.EventId == DeadLetteredEvent);
        await using var host = await LocalHost.StartAsync(
            fail: (consumer, parcel, _) => parcel == "poison" && consumer == "first" ? new PermanentMessageException("never") : null,
            extraLogger: throwing);

        await host.SendAsync(new Parcel("poison"));
        await Eventually.HoldsAsync(() => throwing.Thrown == 1, "the logger threw on the dead letter");
        await host.SendAsync(new Parcel("after"));

        await Eventually.HoldsAsync(
            () => host.Log.Calls.Count(c => c.Parcel == "after") == 2, "the loop kept consuming after the logger threw");
        host.Queue.DeadLettered.ShouldBe(1);
    }

    [Fact]
    public async Task ASourceSupplyingAConsumerWithNoName_FailsAtStart_RatherThanASend()
    {
        // Review finding (#188 P3): an empty consumer name reached the queue and failed a send
        // halfway through its entries. The consumers are read, and checked, at host start.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.local";
            bus.AddLocalConsumerSource(new UnnamedConsumers());
        });
        using var host = builder.Build();

        var fault = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        fault.Message.ShouldContain("invalid consumer");
        fault.Message.ShouldContain("ConsumerType");
    }

    private const int DeadLetteredEvent = 2302;

    private sealed class UnnamedConsumers : ILocalConsumerSource
    {
        public IEnumerable<LocalConsumer> GetConsumers()
            => [new LocalConsumer(typeof(Parcel), string.Empty, typeof(UnnamedConsumers), (_, _, _, _) => ValueTask.CompletedTask)];
    }

    private sealed record Parcel(string Name);

    private sealed record Unconsumed;

    private sealed record Call(string Consumer, string Parcel, Guid Scope);

    private sealed class CallLog
    {
        private readonly ConcurrentDictionary<(string, string), int> _counts = new();

        public ConcurrentQueue<Call> Calls { get; } = new();

        public int Next(string consumer, string parcel) => _counts.AddOrUpdate((consumer, parcel), 1, static (_, n) => n + 1);
    }

    /// <summary>Two consumers of <see cref="Parcel"/>; <c>fail</c> scripts their exceptions.</summary>
    private sealed class ParcelConsumers(Func<string, string, int, Exception?> fail) : ILocalConsumerSource
    {
        public IEnumerable<LocalConsumer> GetConsumers() =>
        [
            Consumer("first"),
            Consumer("second"),
        ];

        private LocalConsumer Consumer(string name) => new(
            typeof(Parcel),
            $"tests.local.{name}",
            typeof(ParcelConsumers),
            (services, message, _, _) =>
            {
                var parcel = ((Parcel)message).Name;
                var log = services.GetRequiredService<CallLog>();
                log.Calls.Enqueue(new Call(name, parcel, services.GetRequiredService<ScopeMarker>().Id));
                return fail(name, parcel, log.Next(name, parcel)) is { } failure
                    ? ValueTask.FromException(failure)
                    : ValueTask.CompletedTask;
            });
    }

    private sealed class LocalHost(IHost host) : IAsyncDisposable
    {
        public IServiceProvider Services => host.Services;

        public CallLog Log => Services.GetRequiredService<CallLog>();

        public LocalQueue Queue => Services.GetRequiredService<LocalQueue>();

        public CapturingLoggerProvider Logs => Services.GetRequiredService<CapturingLoggerProvider>();

        public static async Task<LocalHost> StartAsync(
            Func<string, string, int, Exception?>? fail = null, ILoggerProvider? extraLogger = null)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            var logs = new CapturingLoggerProvider();
            builder.Logging.AddProvider(logs);
            if (extraLogger is not null)
            {
                builder.Logging.AddProvider(extraLogger);
            }

            builder.Services.AddSingleton(logs);
            builder.Services.AddSingleton<CallLog>();
            builder.Services.AddScoped<ScopeMarker>();
            builder.Services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = "tests.local";
                bus.AddLocalConsumerSource(new ParcelConsumers(fail ?? ((_, _, _) => null)));
            });

            var built = builder.Build();
            await built.StartAsync();
            return new LocalHost(built);
        }

        public const string Sentinel = "sentinel";

        /// <summary>
        /// Waits until everything sent so far has settled: the worker reads one entry at a time, in
        /// order, so once a later message reached both consumers nothing earlier is still queued.
        /// </summary>
        public async Task SettleAsync()
        {
            await SendAsync(new Parcel(Sentinel));
            await Eventually.HoldsAsync(
                () => Log.Calls.Count(c => c.Parcel == Sentinel) == 2, "the sentinel reached both consumers");
        }

        public async Task SendAsync(object message)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILocalMessageSender>()
                .SendAsync(message, MessageId.New(), DateTimeOffset.UtcNow);
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
