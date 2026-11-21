// NSubstitute ValueTask setup chains (.Returns on the method-call result) are intentional.
#pragma warning disable CA2012
namespace MicroKit.Messaging.UnitTests.Processing;

using Microsoft.Extensions.Hosting;
using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Local;
using MicroKit.Messaging.Transport;
using MicroKit.Messaging.UnitTests.Bus;

/// <summary>
/// Every long-running loop runs under the one loop guard (#188, <see cref="GuardedLoop"/>): an
/// <see cref="ObjectDisposedException"/> while the host runs, and a logger that throws, both leave the
/// loop running — only a host stop ends it.
/// </summary>
public sealed class GuardedLoopTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly MessageStoreKey Store = new("tests:guard");

    public enum Fault
    {
        /// <summary>The first pass meets an ObjectDisposedException naming the provider, the host running.</summary>
        DisposedWhileRunning,

        /// <summary>The first pass fails, and the loop's logger throws on every call.</summary>
        LoggerThatThrows,
    }

    public static TheoryData<string, Fault> Loops()
    {
        var data = new TheoryData<string, Fault>();
        foreach (var loop in new[] { "outbox", "inbox", "outbox-retention", "inbox-retention", "local-queue", "in-memory-transport" })
        {
            data.Add(loop, Fault.DisposedWhileRunning);
            data.Add(loop, Fault.LoggerThatThrows);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task AFault_WhileTheHostRuns_LeavesTheLoopRunning(string loop, Fault fault)
    {
        var probe = new Probe(fault);
        await using var run = loop switch
        {
            "outbox" => StoreLoop(probe, (sf, log) => new OutboxLoop(sf, Store, OutboxOptions, Logger<OutboxLoop>(log)).RunAsync, s =>
            {
                var processor = Substitute.For<IOutboxProcessor>();
                processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(_ => probe.Pass(OutboxBatchResult.Empty));
                s.AddKeyedSingleton(Store, processor);
            }),
            "inbox" => StoreLoop(probe, (sf, log) => new InboxLoop(sf, Store, InboxOptions, Logger<InboxLoop>(log)).RunAsync, s =>
            {
                var processor = Substitute.For<IInboxProcessor>();
                processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(_ => probe.Pass(InboxBatchResult.Empty));
                s.AddKeyedSingleton(Store, processor);
            }),
            "outbox-retention" => StoreLoop(probe, (sf, log) => new OutboxRetentionLoop(sf, Store, OutboxOptions, TimeProvider.System, Logger<OutboxRetentionLoop>(log)).RunAsync, s =>
            {
                var retention = Substitute.For<IOutboxRetentionStore>();
                retention.DeleteProcessedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(_ => probe.Pass(0));
                s.AddKeyedSingleton(Store, retention);
            }),
            "inbox-retention" => StoreLoop(probe, (sf, log) => new InboxRetentionLoop(sf, Store, InboxOptions, TimeProvider.System, Logger<InboxRetentionLoop>(log)).RunAsync, s =>
            {
                var retention = Substitute.For<IInboxRetentionStore>();
                retention.DeleteProcessedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(_ => probe.Pass(0));
                s.AddKeyedSingleton(Store, retention);
            }),
            "local-queue" => await LocalQueueAsync(probe),
            "in-memory-transport" => await InMemoryTransportAsync(probe),
            _ => throw new ArgumentOutOfRangeException(nameof(loop)),
        };

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (probe.Passes < 1)
        {
            run.Loop.IsCompleted.ShouldBeFalse($"the {loop} loop ended on a {fault} while the host ran");
            waited.Elapsed.ShouldBeLessThan(Wait, $"the {loop} loop never ran again after a {fault}");
            await Task.Delay(10);
        }

        run.Loop.IsCompleted.ShouldBeFalse($"the {loop} loop is still running");
    }

    [Fact]
    public void AFaultOfAnInMemoryWorkersLoop_IsReported_NeverSwallowed()
    {
        // The callbacks use only their logger; the other dependencies are never reached.
        var queueLog = new CapturingLogger<LocalQueueWorker>();
        var transportLog = new CapturingLogger<InMemoryTransportWorker>();
        var fault = new InvalidOperationException("the queue itself failed");

        new LocalQueueWorker(null!, null!, TimeProvider.System, queueLog).OnLoopFault(fault)
            .ShouldBe(TimeSpan.FromSeconds(1));
        new InMemoryTransportWorker(new InMemoryBroker(), null!, TimeProvider.System, transportLog).OnLoopFault(fault)
            .ShouldBe(TimeSpan.FromSeconds(1));

        queueLog.Records.ShouldContain(r => r.EventId == 2305 && r.Level == LogLevel.Error && r.Exception == fault);
        transportLog.Records.ShouldContain(r => r.EventId == 3017 && r.Level == LogLevel.Error && r.Exception == fault);
    }

    [Fact]
    public void AnUnsettledDeliveryWhileTheHostRuns_IsAnOutage_OnlyWhenTheRootContainerIsDisposed()
    {
        // The callers use only their scope factory; the other dependencies are never reached.
        var unsettled = new DeliveryOutcome(DeliveryVerdict.Unsettled, new ObjectDisposedException(nameof(IServiceProvider)));
        var disposedRoot = Substitute.For<IServiceScopeFactory>();
        disposedRoot.CreateScope().Returns(_ => throw new ObjectDisposedException(nameof(IServiceProvider)));
        var healthyRoot = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        new LocalQueueWorker(null!, disposedRoot, TimeProvider.System, NullLogger<LocalQueueWorker>.Instance)
            .WhileTheHostRuns(unsettled).Verdict.ShouldBe(DeliveryVerdict.DependencyUnavailable, "the container is gone: pause, charge nothing");
        new InMemoryTransportWorker(new InMemoryBroker(), disposedRoot, TimeProvider.System, NullLogger<InMemoryTransportWorker>.Instance)
            .WhileTheHostRuns(unsettled).Verdict.ShouldBe(DeliveryVerdict.DependencyUnavailable);

        new LocalQueueWorker(null!, healthyRoot, TimeProvider.System, NullLogger<LocalQueueWorker>.Instance)
            .WhileTheHostRuns(unsettled).Verdict.ShouldBe(DeliveryVerdict.Transient, "the container is fine: the handler's own fault, counted");
        new InMemoryTransportWorker(new InMemoryBroker(), healthyRoot, TimeProvider.System, NullLogger<InMemoryTransportWorker>.Instance)
            .WhileTheHostRuns(unsettled).Verdict.ShouldBe(DeliveryVerdict.Transient);
    }

    [Fact]
    public void AContainerProbeThatFailsAnyOtherWay_IsAnUnusableContainer_AndNeverEscapes()
    {
        // A disposed DryIoc container throws its own ContainerException: escaping here, after the
        // dequeue and before the settlement, would lose the message (#188, review of 1c70bf5).
        var unsettled = new DeliveryOutcome(DeliveryVerdict.Unsettled, new ObjectDisposedException(nameof(IServiceProvider)));
        var foreignContainer = Substitute.For<IServiceScopeFactory>();
        foreignContainer.CreateScope().Returns(_ => throw new InvalidOperationException("container is disposed"));

        new LocalQueueWorker(null!, foreignContainer, TimeProvider.System, NullLogger<LocalQueueWorker>.Instance)
            .WhileTheHostRuns(unsettled).Verdict.ShouldBe(DeliveryVerdict.DependencyUnavailable);
        new InMemoryTransportWorker(new InMemoryBroker(), foreignContainer, TimeProvider.System, NullLogger<InMemoryTransportWorker>.Instance)
            .WhileTheHostRuns(unsettled).Verdict.ShouldBe(DeliveryVerdict.DependencyUnavailable);
    }

    [Fact]
    public async Task TheLocalQueue_AHandlersOwnDisposedProvider_WithTheContainerHealthy_IsCounted_AndDeadLetters()
    {
        var calls = 0;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var queue = new LocalQueue { DependencyUnavailableBackoff = TimeSpan.FromSeconds(30) };
        builder.Services.AddSingleton(queue);
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.guard";
            bus.AddLocalConsumerSource(new AlwaysDisposedConsumers(() => Interlocked.Increment(ref calls)));
        });
        using var host = builder.Build();
        await host.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILocalMessageSender>()
                .SendAsync(new ProbeMessage(), MessageId.New(), DateTimeOffset.UtcNow);
        }

        // A pause would be 30 s: dead-lettering inside the wait proves it was counted, not paused.
        await Eventually.HoldsAsync(() => queue.DeadLettered == 1, "dead-lettered at the attempt limit, never retried for ever");
        Volatile.Read(ref calls).ShouldBe(queue.MaxDeliveryAttempts);
        await host.StopAsync();
    }

    [Fact]
    public async Task TheInMemoryTransport_AHandlersOwnDisposedProvider_WithTheContainerHealthy_IsCounted_AndDeadLetters()
    {
        var broker = new InMemoryBroker { DependencyUnavailableBackoff = TimeSpan.FromSeconds(30) };
        var counter = new DisposedCounter();
        var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<AlwaysDisposedHandler>(),
            broker,
            services: s => s.AddSingleton(counter));
        await using (host)
        {
            await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

            await Eventually.HoldsAsync(() => broker.GetDeadLetters(BusTestHost.Source).Count == 1,
                "dead-lettered at the attempt limit, never retried for ever");
            counter.Calls.ShouldBe(broker.MaxDeliveryAttempts);
        }
    }

    private sealed class AlwaysDisposedConsumers(Action called) : ILocalConsumerSource
    {
        public IEnumerable<LocalConsumer> GetConsumers() =>
        [
            new LocalConsumer(typeof(ProbeMessage), "tests.guard.disposed", typeof(AlwaysDisposedConsumers), (_, _, _, _) =>
            {
                called();
                return ValueTask.FromException(new ObjectDisposedException(nameof(IServiceProvider)));
            }),
        ];
    }

    public sealed class DisposedCounter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Called() => Interlocked.Increment(ref _calls);
    }

    private sealed class AlwaysDisposedHandler(DisposedCounter counter) : IMessageHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
        {
            counter.Called();
            return ValueTask.FromException(new ObjectDisposedException(nameof(IServiceProvider)));
        }
    }

    private static OutboxProcessorOptions OutboxOptions => new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(5),
        MaxPollingInterval = TimeSpan.FromMilliseconds(10),
        TransportUnavailableBackoff = TimeSpan.FromMilliseconds(10),
        RetentionDays = 1,
        RetentionInterval = TimeSpan.FromMilliseconds(10),
    };

    private static InboxProcessorOptions InboxOptions => new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(5),
        MaxPollingInterval = TimeSpan.FromMilliseconds(10),
        DependencyUnavailableBackoff = TimeSpan.FromMilliseconds(10),
        RetentionDays = 1,
        RetentionInterval = TimeSpan.FromMilliseconds(10),
    };

    private static ILogger<T> Logger<T>(bool throwing) => throwing ? new ThrowingLogger<T>() : NullLogger<T>.Instance;

    /// <summary>A store loop over a scope factory whose keyed service the probe scripts.</summary>
    private static Running StoreLoop(
        Probe probe,
        Func<IServiceScopeFactory, bool, Func<CancellationToken, Task>> loop,
        Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        var provider = services.BuildServiceProvider();
        var cts = new CancellationTokenSource();
        var task = loop(provider.GetRequiredService<IServiceScopeFactory>(), probe.Fault is Fault.LoggerThatThrows)(cts.Token);
        return new Running(task, cts, async () => await provider.DisposeAsync());
    }

    private static async Task<Running> LocalQueueAsync(Probe probe)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(new LocalQueue { DependencyUnavailableBackoff = TimeSpan.FromMilliseconds(10) });
        if (probe.Fault is Fault.LoggerThatThrows)
        {
            builder.Services.AddSingleton<ILogger<LocalQueueWorker>>(new ThrowingLogger<LocalQueueWorker>());
        }

        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.guard";
            bus.AddLocalConsumerSource(new ProbeConsumers(probe));
        });
        var host = builder.Build();
        await host.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILocalMessageSender>()
                .SendAsync(new ProbeMessage(), MessageId.New(), DateTimeOffset.UtcNow);
        }

        return HostRun(host, typeof(LocalQueueWorker));
    }

    private static async Task<Running> InMemoryTransportAsync(Probe probe)
    {
        var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<ProbeHandler>(),
            new InMemoryBroker { DependencyUnavailableBackoff = TimeSpan.FromMilliseconds(10) },
            services: s =>
            {
                s.AddSingleton(probe);
                if (probe.Fault is Fault.LoggerThatThrows)
                {
                    s.AddSingleton<ILogger<InMemoryTransportWorker>>(new ThrowingLogger<InMemoryTransportWorker>());
                }
            });
        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        var worker = host.Provider.GetServices<IHostedService>().OfType<InMemoryTransportWorker>().Single();
        return new Running(worker.ExecuteTask!, null, async () => await host.DisposeAsync());
    }

    private static Running HostRun(IHost host, Type worker)
    {
        var service = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().Single(s => s.GetType() == worker);
        return new Running(service.ExecuteTask!, null, async () =>
        {
            await host.StopAsync();
            host.Dispose();
        });
    }

    /// <summary>The first pass faults; every pass after it is counted.</summary>
    public sealed class Probe(Fault fault)
    {
        private int _calls;
        private int _passes;

        public Fault Fault { get; } = fault;

        public int Passes => Volatile.Read(ref _passes);

        public Exception? Next()
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Fault is Fault.DisposedWhileRunning
                    ? new ObjectDisposedException(nameof(IServiceProvider))
                    : new InvalidOperationException("the first pass fails, and logging it throws");
            }

            Interlocked.Increment(ref _passes);
            return null;
        }

        public ValueTask<T> Pass<T>(T result)
        {
            var fault = Next();
            return fault is null ? ValueTask.FromResult(result) : ValueTask.FromException<T>(fault);
        }
    }

    private sealed class Running(Task loop, CancellationTokenSource? cts, Func<Task> dispose) : IAsyncDisposable
    {
        public Task Loop { get; } = loop;

        public async ValueTask DisposeAsync()
        {
            if (cts is not null)
            {
                await cts.CancelAsync();
                (await Task.WhenAny(Loop, Task.Delay(Wait))).ShouldBe(Loop, "the loop ends when the host stops");
                cts.Dispose();
            }

            await dispose();
        }
    }

    private sealed record ProbeMessage;

    private sealed class ProbeConsumers(Probe probe) : ILocalConsumerSource
    {
        public IEnumerable<LocalConsumer> GetConsumers() =>
        [
            new LocalConsumer(typeof(ProbeMessage), "tests.guard.probe", typeof(ProbeConsumers), (_, _, _, _) =>
            {
                var fault = probe.Next();
                return fault is null ? ValueTask.CompletedTask : ValueTask.FromException(fault);
            }),
        ];
    }

    private sealed class ProbeHandler(Probe probe) : IMessageHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
        {
            var fault = probe.Next();
            return fault is null ? ValueTask.CompletedTask : ValueTask.FromException(fault);
        }
    }
}

/// <summary>A logger whose every call throws: a logging provider that has failed.</summary>
internal sealed class ThrowingLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => throw new InvalidOperationException("The logging provider failed.");
}
