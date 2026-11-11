namespace MicroKit.Messaging.UnitTests.Bus;

using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

/// <summary>
/// A generic host with the bus, the in-memory transport and nothing else: no <c>DbContext</c>, no
/// store, no EF Core reference in this test project at all. Started and stopped by the real
/// <see cref="IHost"/>, so the hosted lifecycle — the composition validator and the endpoint
/// binding in <c>StartingAsync</c> — runs exactly as in production (#187).
/// </summary>
internal sealed class BusTestHost : IAsyncDisposable
{
    /// <summary>The service name of a test host: its endpoint, and the source of what it publishes.</summary>
    internal const string Source = "/tests/orders";

    private readonly IHost _host;
    private bool _stopped;

    private BusTestHost(IHost host, InMemoryBroker broker, string serviceName)
    {
        _host = host;
        Broker = broker;
        ServiceName = serviceName;
    }

    public IServiceProvider Provider => _host.Services;

    public InMemoryBroker Broker { get; }

    /// <summary>Gets this host's service name, which is also its endpoint on the broker.</summary>
    public string ServiceName { get; }

    public BusRecorder Recorder => Provider.GetRequiredService<BusRecorder>();

    /// <summary>Gets this host's dead letters on the broker.</summary>
    public IReadOnlyList<MessageEnvelope> DeadLetters => Broker.GetDeadLetters(ServiceName);

    /// <summary>Gets the failed attempts this host's endpoint recorded for a message.</summary>
    public int FailedAttempts(string messageId) => Broker.GetFailedAttempts(ServiceName, messageId);

    /// <summary>Composes and starts a host.</summary>
    /// <param name="compose">Adds handlers, middleware and options to the builder.</param>
    /// <param name="broker">A broker to share with another host; a new one when omitted.</param>
    /// <param name="services">Extra registrations made before <c>AddMicroKitMessaging()</c>.</param>
    /// <param name="serviceName">The host's service name; <see cref="Source"/> when omitted.</param>
    public static async Task<BusTestHost> StartAsync(
        Action<MessagingBuilder> compose,
        InMemoryBroker? broker = null,
        Action<IServiceCollection>? services = null,
        string serviceName = Source)
    {
        var host = Build(compose, broker, services, serviceName, out _);
        await host._host.StartAsync().ConfigureAwait(false);
        return host;
    }

    /// <summary>Composes a host without starting it.</summary>
    public static BusTestHost Build(
        Action<MessagingBuilder> compose,
        InMemoryBroker? broker,
        Action<IServiceCollection>? services,
        string serviceName,
        out InMemoryBroker used)
    {
        used = broker ?? new InMemoryBroker();
        var shared = used;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = "MicroKit.Messaging.UnitTests",
        });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));

        builder.Services.AddSingleton<BusRecorder>();
        builder.Services.AddScoped<ScopeMarker>();
        services?.Invoke(builder.Services);

        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = serviceName;
            bus.Publishes<OrderPlaced>().Publishes<OrderShipped>().Publishes<InvoiceIssued>();
            bus.UseInMemoryTransport(shared);
            compose(bus);
        });

        return new BusTestHost(builder.Build(), shared, serviceName);
    }

    /// <summary>Starts a host built with <see cref="Build"/>.</summary>
    public Task StartHostAsync() => _host.StartAsync();

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
        await _host.StopAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _host.Dispose();
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
