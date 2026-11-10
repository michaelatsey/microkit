namespace MicroKit.Messaging.UnitTests.Bus;

using Microsoft.Extensions.Hosting;

/// <summary>
/// How the in-memory transport settles each verdict (#186, decision 10), and what the composition
/// registers — in particular that only the features that need one register a hosted service.
/// </summary>
public sealed class InMemoryTransportTests
{
    [Fact]
    public async Task ATransientFailure_IsRedeliveredUpToTheBound_ThenDeadLettered()
    {
        var broker = new InMemoryBroker { MaxDeliveryAttempts = 3 };
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddMessageHandler<FlakyOrderPlacedHandler, OrderPlaced>(),
            broker,
            services => services.AddSingleton(new FlakyScript { FailuresBeforeSuccess = int.MaxValue }));

        var id = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        await Eventually.HoldsAsync(() => broker.DeadLetters.Count == 1, "dead-lettered at the bound");

        broker.GetFailedAttempts(id.ToString()).ShouldBe(3);
        host.Provider.GetRequiredService<FlakyScript>().Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task ATransientFailure_BelowTheBound_IsRedeliveredAndSucceeds()
    {
        var broker = new InMemoryBroker { MaxDeliveryAttempts = 3 };
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddMessageHandler<FlakyOrderPlacedHandler, OrderPlaced>(),
            broker,
            services => services.AddSingleton(new FlakyScript { FailuresBeforeSuccess = 1 }));

        var id = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        var script = host.Provider.GetRequiredService<FlakyScript>();
        await Eventually.HoldsAsync(() => script.Succeeded, "the redelivery succeeds");

        broker.GetFailedAttempts(id.ToString()).ShouldBe(1);
        broker.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADependencyUnavailable_PausesTheEndpoint_AndRedeliversWithoutCountingAnAttempt()
    {
        var broker = new InMemoryBroker
        {
            MaxDeliveryAttempts = 1,
            DependencyUnavailableBackoff = TimeSpan.FromMilliseconds(50),
        };
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddMessageHandler<FlakyOrderPlacedHandler, OrderPlaced>(),
            broker,
            services => services.AddSingleton(new FlakyScript
            {
                FailuresBeforeSuccess = 2,
                Failure = () => new InboxDependencyUnavailableException("downstream down"),
            }));

        var id = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        var script = host.Provider.GetRequiredService<FlakyScript>();
        await Eventually.HoldsAsync(() => script.Succeeded, "delivered once the dependency is back");

        // MaxDeliveryAttempts = 1: had either outage counted, the message would be dead.
        broker.GetFailedAttempts(id.ToString()).ShouldBe(0);
        broker.DeadLetters.ShouldBeEmpty();
        script.Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task APermanentFailure_IsDeadLetteredOnTheFirstAttempt()
    {
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddMessageHandler<FlakyOrderPlacedHandler, OrderPlaced>(),
            services: services => services.AddSingleton(new FlakyScript
            {
                FailuresBeforeSuccess = int.MaxValue,
                Failure = () => new PermanentMessageException("contract violated"),
            }));

        var id = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        await Eventually.HoldsAsync(() => host.Broker.DeadLetters.Count == 1, "dead-lettered");

        host.Provider.GetRequiredService<FlakyScript>().Attempts.ShouldBe(1);
        host.Broker.GetFailedAttempts(id.ToString()).ShouldBe(0);
    }

    [Fact]
    public async Task AHandlersOwnObjectDisposedException_IsTransient_AndTheLoopKeepsConsuming()
    {
        var broker = new InMemoryBroker { MaxDeliveryAttempts = 2 };
        await using var host = await BusTestHost.StartAsync(
            bus => bus
                .AddMessageHandler<FlakyOrderPlacedHandler, OrderPlaced>()
                .AddMessageHandler<OrderShippedHandler, OrderShipped>(),
            broker,
            services => services.AddSingleton(new FlakyScript
            {
                FailuresBeforeSuccess = int.MaxValue,
                Failure = () => new ObjectDisposedException("HttpClient"),
            }));

        var id = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await Eventually.HoldsAsync(() => broker.DeadLetters.Count == 1, "bounded, then dead-lettered");
        broker.GetFailedAttempts(id.ToString()).ShouldBe(2);

        // The loop is still alive: a later message is delivered.
        await host.PublishAsync(new OrderShipped(Guid.NewGuid()));
        await Eventually.HoldsAsync(
            () => host.Recorder.Deliveries.Any(d => d.Handler == nameof(OrderShippedHandler)),
            "the consume loop survived the handler's ObjectDisposedException");
    }

    [Fact]
    public void AddMicroKitMessaging_RegistersNoHostedService()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging();

        services.ShouldNotContain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void UseInMemoryTransport_RegistersOnlyItsConsumeLoop_AndIsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging().UseInMemoryTransport().UseInMemoryTransport();

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .ShouldHaveSingleItem().ImplementationType!.Name.ShouldBe("InMemoryTransportWorker");
        services.Count(d => d.ServiceType == typeof(IMessageTransport)).ShouldBe(1);
    }

    [Fact]
    public void UseInMemoryTransport_WithASuppliedBroker_UsesThatBroker()
    {
        var broker = new InMemoryBroker();
        var services = new ServiceCollection();

        services.AddMicroKitMessaging().UseInMemoryTransport().UseInMemoryTransport(broker);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<InMemoryBroker>().ShouldBeSameAs(broker);
    }

    [Fact]
    public void UseInMemoryTransport_WhenAnotherTransportIsRegistered_RefusesRatherThanShadowingIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageTransport, RecordingMessageTransport>();

        var ex = Should.Throw<InvalidOperationException>(
            () => services.AddMicroKitMessaging().UseInMemoryTransport());

        ex.Message.ShouldContain(nameof(RecordingMessageTransport));
    }

    [Fact]
    public void AddOutboxInboxProcessing_RegistersTheFourWorkers_AndIsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging().AddOutboxInboxProcessing().AddOutboxInboxProcessing();

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType!.Name)
            .ShouldBe(
                ["OutboxWorker", "OutboxRetentionWorker", "InboxWorker", "InboxRetentionWorker"],
                ignoreOrder: true);
        services.Count(d => d.ServiceType == typeof(IOutboxProcessor)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IInboxCoordinator)).ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InMemoryBroker_RejectsABoundBelowOne(int bound)
        => Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryBroker { MaxDeliveryAttempts = bound });
}

/// <summary>Fails a set number of times, then succeeds.</summary>
internal sealed class FlakyScript
{
    private int _attempts;

    public int FailuresBeforeSuccess { get; init; }

    public Func<Exception> Failure { get; init; } = () => new InvalidOperationException("transient");

    public int Attempts => Volatile.Read(ref _attempts);

    public bool Succeeded { get; private set; }

    public Exception? Next()
    {
        var attempt = Interlocked.Increment(ref _attempts);
        if (attempt <= FailuresBeforeSuccess)
        {
            return Failure();
        }

        Succeeded = true;
        return null;
    }
}

internal sealed class FlakyOrderPlacedHandler(FlakyScript script) : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
        => script.Next() is { } fault ? ValueTask.FromException(fault) : ValueTask.CompletedTask;
}
