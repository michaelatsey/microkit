namespace MicroKit.Messaging.UnitTests.Bus;

using Microsoft.Extensions.Hosting;

/// <summary>
/// The consume loop's safety net (#186, PR #202 review): it settles before it logs, a failing
/// logging provider never escapes the loop, and a delivery is settled at most once.
/// </summary>
public sealed class SafetyNetTests
{
    [Fact]
    public async Task WithAThrowingLogger_AFaultInTheDeliveryPath_DeadLettersOnce_AndTheLoopKeepsConsuming()
    {
        // The pipeline logs HandlerFailed (3002) when the handler throws; the provider throws on
        // it, so the fault escapes the pipeline — and every log of the safety net throws too.
        var logs = new ThrowingLoggerProvider(e => e.Level >= LogLevel.Warning && e.Category.StartsWith(
            "MicroKit.Messaging.", StringComparison.Ordinal));
        await using var host = await BusTestHost.StartAsync(
            bus => bus
                .AddHandler<FlakyOrderPlacedHandler>()
                .AddHandler<OrderShippedHandler>(),
            services: services => services
                .AddSingleton<ILoggerProvider>(logs)
                .AddSingleton(new FlakyScript
                {
                    FailuresBeforeSuccess = 1,
                    Failure = () => new PermanentMessageException("contract violated"),
                }));

        var faulted = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await Eventually.HoldsAsync(() => host.DeadLetters.Count >= 1, "dead-lettered");

        // A later message is consumed: the loop survived its own failing logger.
        await host.PublishAsync(new OrderShipped(Guid.NewGuid()));
        await Eventually.HoldsAsync(
            () => host.Recorder.Deliveries.Any(d => d.Handler == nameof(OrderShippedHandler)),
            "the next message is consumed");

        host.DeadLetters.ShouldHaveSingleItem("dead-lettered exactly once")
            .Id.ShouldBe(faulted.ToString());
        logs.Thrown.ShouldBeGreaterThan(0, "CONTROL: the logger really did throw");
        Worker(host).ExecuteTask.ShouldNotBeNull().IsCompleted
            .ShouldBeFalse("the host keeps running: the consume loop has not ended");
    }

    [Fact]
    public async Task ALoggingFailureAfterARequeue_DoesNotSettleTheMessageASecondTime()
    {
        // EndpointPaused (3012) is logged right after the dependency-unavailable requeue.
        var logs = new ThrowingLoggerProvider(e => e.EventId == 3012);
        var broker = new InMemoryBroker { DependencyUnavailableBackoff = TimeSpan.FromMilliseconds(20) };
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FlakyOrderPlacedHandler>(),
            broker,
            services => services
                .AddSingleton<ILoggerProvider>(logs)
                .AddSingleton(new FlakyScript
                {
                    FailuresBeforeSuccess = 1,
                    Failure = () => new InboxDependencyUnavailableException("downstream down"),
                }));

        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        var script = host.Provider.GetRequiredService<FlakyScript>();
        await Eventually.HoldsAsync(() => script.Succeeded, "redelivered once the dependency is back");

        logs.Thrown.ShouldBe(1, "CONTROL: the log after the requeue threw");
        broker.GetDeadLetters(BusTestHost.Source).ShouldBeEmpty("a requeued message is not dead-lettered as well");
        script.Attempts.ShouldBe(2, "delivered twice: once failing, once from the single requeue");
    }

    [Fact]
    public async Task ALoggingFailureAfterADeadLetter_DoesNotSettleTheMessageASecondTime()
    {
        // DeadLettered (3010) is logged right after the dead-letter at the transient limit.
        var logs = new ThrowingLoggerProvider(e => e.EventId == 3010);
        var broker = new InMemoryBroker { MaxDeliveryAttempts = 1 };
        await using var host = await BusTestHost.StartAsync(
            bus => bus
                .AddHandler<FlakyOrderPlacedHandler>()
                .AddHandler<OrderShippedHandler>(),
            broker,
            services => services
                .AddSingleton<ILoggerProvider>(logs)
                .AddSingleton(new FlakyScript { FailuresBeforeSuccess = int.MaxValue }));

        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await Eventually.HoldsAsync(() => broker.GetDeadLetters(BusTestHost.Source).Count >= 1, "dead-lettered at the limit");

        // A barrier: once the next message is consumed, the first one is fully settled.
        await host.PublishAsync(new OrderShipped(Guid.NewGuid()));
        await Eventually.HoldsAsync(
            () => host.Recorder.Deliveries.Any(d => d.Handler == nameof(OrderShippedHandler)),
            "the next message is consumed");

        logs.Thrown.ShouldBe(1, "CONTROL: the log after the dead-letter threw");
        broker.GetDeadLetters(BusTestHost.Source).Count.ShouldBe(1, "dead-lettered exactly once");
    }

    private static BackgroundService Worker(BusTestHost host)
        => host.Provider.GetServices<IHostedService>().OfType<BackgroundService>().Single();
}

/// <summary>A logging provider whose loggers throw on the entries a predicate selects.</summary>
internal sealed class ThrowingLoggerProvider(Func<ThrowingLoggerProvider.Entry, bool> throwOn) : ILoggerProvider
{
    private readonly Func<Entry, bool> _throwOn = throwOn;
    private int _thrown;

    public int Thrown => Volatile.Read(ref _thrown);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    internal readonly record struct Entry(string Category, LogLevel Level, int EventId);

    private sealed class Logger(ThrowingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (owner._throwOn(new Entry(category, logLevel, eventId.Id)))
            {
                Interlocked.Increment(ref owner._thrown);
                throw new InvalidOperationException($"logging provider failure ({category}, {eventId.Id})");
            }
        }
    }
}
