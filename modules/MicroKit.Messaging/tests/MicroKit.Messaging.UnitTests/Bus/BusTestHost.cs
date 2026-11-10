namespace MicroKit.Messaging.UnitTests.Bus;

using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

/// <summary>
/// A host with the bus, the in-memory transport and nothing else: no <c>DbContext</c>, no store,
/// no EF Core reference in this test project at all. It starts and stops the registered hosted
/// services the way a generic host does.
/// </summary>
internal sealed class BusTestHost : IAsyncDisposable
{
    internal const string Source = "/tests/orders";

    private readonly IHostedService[] _hosted;
    private bool _stopped;

    private BusTestHost(ServiceProvider provider, InMemoryBroker broker)
    {
        Provider = provider;
        Broker = broker;
        _hosted = [.. provider.GetServices<IHostedService>()];
    }

    public ServiceProvider Provider { get; }

    public InMemoryBroker Broker { get; }

    public BusRecorder Recorder => Provider.GetRequiredService<BusRecorder>();

    /// <summary>Composes and starts a host.</summary>
    /// <param name="compose">Adds handlers, middleware and options to the builder.</param>
    /// <param name="broker">A broker to share with another host; a new one when omitted.</param>
    /// <param name="services">Extra registrations made before <c>AddMicroKitMessaging()</c>.</param>
    public static async Task<BusTestHost> StartAsync(
        Action<MessagingBuilder> compose,
        InMemoryBroker? broker = null,
        Action<IServiceCollection>? services = null)
    {
        broker ??= new InMemoryBroker();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<BusRecorder>();
        collection.AddScoped<ScopeMarker>();
        services?.Invoke(collection);

        collection.AddIntegrationEventContracts(
            Source,
            c => c.Publishes<OrderPlaced>().Publishes<OrderShipped>().Publishes<InvoiceIssued>());

        compose(collection.AddMicroKitMessaging().UseInMemoryTransport(broker));

        var provider = collection.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        var host = new BusTestHost(provider, broker);

        foreach (var hosted in host._hosted)
        {
            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return host;
    }

    /// <summary>Publishes from a fresh scope, outside any message.</summary>
    public async Task<MessageId> PublishAsync<T>(T message, PublishOptions? options = null)
        where T : class, IIntegrationEvent
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .PublishAsync(message, options)
            .ConfigureAwait(false);
    }

    /// <summary>Stops the hosted services, as the host does on shutdown.</summary>
    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        foreach (var hosted in _hosted.Reverse())
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Provider.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Polls a condition, so a test waits on an outcome rather than on a fixed delay.</summary>
internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task HoldsAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"Timed out after {Timeout}: {because}");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }
}

/// <summary>What the handlers, middleware and initializers of a test saw, in order.</summary>
internal sealed class BusRecorder
{
    private readonly ConcurrentQueue<string> _steps = new();
    private readonly ConcurrentQueue<Delivery> _deliveries = new();

    public IReadOnlyList<string> Steps => [.. _steps];

    public IReadOnlyList<Delivery> Deliveries => [.. _deliveries];

    public void Step(string step) => _steps.Enqueue(step);

    public void Delivered(string handler, object message, MessageContext context, Guid scopeId)
        => _deliveries.Enqueue(new Delivery(handler, message, context, scopeId));

    internal sealed record Delivery(string Handler, object Message, MessageContext Context, Guid ScopeId);
}

/// <summary>One instance per DI scope; its id tells two scopes apart.</summary>
internal sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

[MessageContract("tests.bus.order-placed")]
internal sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

[MessageContract("tests.bus.order-shipped")]
internal sealed record OrderShipped(Guid OrderId) : IIntegrationEvent;

[MessageContract("tests.bus.invoice-issued")]
internal sealed record InvoiceIssued(Serialization.Money Total) : IIntegrationEvent;

/// <summary>Records every delivery with its context and its scope.</summary>
internal sealed class FirstOrderPlacedHandler(BusRecorder recorder, ScopeMarker scope)
    : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Step(nameof(FirstOrderPlacedHandler));
        recorder.Delivered(nameof(FirstOrderPlacedHandler), message, context, scope.Id);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The second handler of the same contract.</summary>
internal sealed class SecondOrderPlacedHandler(BusRecorder recorder, ScopeMarker scope)
    : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Step(nameof(SecondOrderPlacedHandler));
        recorder.Delivered(nameof(SecondOrderPlacedHandler), message, context, scope.Id);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Records <see cref="OrderShipped"/> deliveries.</summary>
internal sealed class OrderShippedHandler(BusRecorder recorder, ScopeMarker scope)
    : IMessageHandler<OrderShipped>
{
    public ValueTask HandleAsync(OrderShipped message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered(nameof(OrderShippedHandler), message, context, scope.Id);
        return ValueTask.CompletedTask;
    }
}
