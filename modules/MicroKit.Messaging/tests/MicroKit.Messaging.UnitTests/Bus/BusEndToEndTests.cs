namespace MicroKit.Messaging.UnitTests.Bus;

using System.Diagnostics;
using MicroKit.Messaging.UnitTests.Serialization;
using Microsoft.Extensions.Hosting;

/// <summary>
/// The Verification list of #186, one test per bullet, on a host with no <c>DbContext</c> and the
/// in-memory transport. This project references no EF Core package, so "no persistence" holds by
/// construction, not by configuration.
/// </summary>
public sealed class BusEndToEndTests
{
    // ── Verification 1 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_WithNoDbContextAndTheInMemoryTransport_ReachesBothHandlersWithAFullyPopulatedContext()
    {
        await using var host = await BusTestHost.StartAsync(bus => bus
            .AddHandler<FirstOrderPlacedHandler>()
            .AddHandler<SecondOrderPlacedHandler>());

        // The bus owns the only worker: AddMicroKitMessaging() registers no hosted service but
        // its composition validator (#186, #187).
        host.Provider.GetServices<IHostedService>().Select(s => s.GetType().Name)
            .ShouldBe(["MessagingCompositionValidator", "InMemoryTransportWorker"]);
        host.Provider.GetService<IOutboxWriter>().ShouldBeNull("no store is composed");
        host.Provider.GetService<IInboxWriter>().ShouldBeNull("no store is composed");

        var order = new OrderPlaced(Guid.NewGuid());
        MessageId id;
        string traceParent;
        using (var activity = new Activity("checkout").Start())
        {
            traceParent = activity.Id!;
            id = await host.PublishAsync(order, new PublishOptions
            {
                TenantId = "tenant-a",
                Headers = new Dictionary<string, string> { ["x-origin"] = "checkout" },
            });
        }

        await Eventually.HoldsAsync(() => host.Recorder.Deliveries.Count == 2, "both handlers ran");

        var deliveries = host.Recorder.Deliveries;
        deliveries.Select(d => d.Handler).ShouldBe(
            [nameof(FirstOrderPlacedHandler), nameof(SecondOrderPlacedHandler)], ignoreOrder: true);
        deliveries.Select(d => d.ScopeId).Distinct().Count()
            .ShouldBe(2, "each handler of an endpoint runs in its own scope");

        foreach (var delivery in deliveries)
        {
            delivery.Message.ShouldBe(order);

            var context = delivery.Context;
            context.MessageId.ShouldBe(id);
            context.ContractName.ShouldBe("tests.bus.order-placed");
            context.ContractVersion.ShouldBe(1);
            context.Source.ShouldBe(BusTestHost.Source);
            context.OccurredOnUtc.ShouldNotBe(default);
            context.TenantId.ShouldBe("tenant-a");
            context.CorrelationId.ShouldNotBeNullOrWhiteSpace();
            context.CausationId.ShouldBeNull("a root publication has no cause");
            context.Headers["x-origin"].ShouldBe("checkout");
            context.Headers[MessageHeaders.TenantId].ShouldBe("tenant-a");
            context.Headers[MessageHeaders.CorrelationId].ShouldBe(context.CorrelationId);
            context.Headers[MessageHeaders.TraceParent].ShouldBe(traceParent);
        }

        deliveries[0].Context.CorrelationId.ShouldBe(deliveries[1].Context.CorrelationId);
    }

    // ── Verification 2 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_WhenTheBodyCannotBeDeserialized_DeadLettersOnFirstDeliveryAndNeverReachesAHandler()
    {
        await using var host = await BusTestHost.StartAsync(bus => bus
            .AddHandler<FirstOrderPlacedHandler>()
            .AddReceiveMiddleware<RecordingReceiveMiddleware>());

        var poison = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Source = "/elsewhere",
            Type = "tests.bus.order-placed.v1",
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Body = "{ this is not json"u8.ToArray(),
        };

        await host.Provider.GetRequiredService<IMessageTransport>().SendAsync(poison);

        await Eventually.HoldsAsync(() => host.DeadLetters.Count == 1, "the body is dead-lettered");

        host.DeadLetters.ShouldHaveSingleItem().Id.ShouldBe(poison.Id);
        host.FailedAttempts(poison.Id)
            .ShouldBe(0, "dead-lettered on the FIRST delivery, not after exhausting retries");
        host.Recorder.Deliveries.ShouldBeEmpty("no handler is reached");
        host.Recorder.Steps.ShouldBeEmpty("not even a message scope is created for it");
    }

    // ── Verification 3 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureJson_AHostConverter_IsUsedOnSerializeAndOnDeserialize()
    {
        var converter = new CountingMoneyConverter();
        await using var host = await BusTestHost.StartAsync(bus => bus
            .ConfigureJson(json => json.Converters.Add(converter))
            .AddHandler<InvoiceIssuedHandler>());

        await host.PublishAsync(new InvoiceIssued(new Money(12)));

        await Eventually.HoldsAsync(() => host.Recorder.Deliveries.Count == 1, "the handler ran");

        host.Recorder.Deliveries.Single().Message.ShouldBe(new InvoiceIssued(new Money(12)));
        converter.Writes.ShouldBe(1, "the host's converter wrote the body on publish");
        converter.Reads.ShouldBe(1, "and read it back on receive");
    }

    // ── Verification 4 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_RegisteredByTheHost_RunsOnSendAndOnReceive()
    {
        await using var host = await BusTestHost.StartAsync(bus => bus
            .AddSendMiddleware<RecordingSendMiddleware>()
            .AddReceiveMiddleware<RecordingReceiveMiddleware>()
            .AddHandler<FirstOrderPlacedHandler>());

        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        await Eventually.HoldsAsync(() => host.Recorder.Deliveries.Count == 1, "the handler ran");

        host.Recorder.Steps.ShouldBe(
        [
            "send:tests.bus.order-placed.v1",
            $"receive:{nameof(FirstOrderPlacedHandler)}:stamped",
            nameof(FirstOrderPlacedHandler),
        ]);
    }

    // ── Verification 5 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostShutdown_MidHandler_RedeliversToTheNextHostWithoutCountingAFailedAttempt()
    {
        var broker = new InMemoryBroker();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        MessageId id;
        await using (var first = await BusTestHost.StartAsync(
            bus => bus.AddHandler<BlockingOrderPlacedHandler>(),
            broker,
            services => services.AddSingleton(started)))
        {
            id = await first.PublishAsync(new OrderPlaced(Guid.NewGuid()));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // The host stops while the handler is still running: its token is cancelled.
            await first.StopAsync();
        }

        broker.GetFailedAttempts(BusTestHost.Source, id.ToString()).ShouldBe(0, "shutdown is not a failed attempt");
        broker.GetDeadLetters(BusTestHost.Source).ShouldBeEmpty();

        await using var second = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            broker);

        await Eventually.HoldsAsync(
            () => second.Recorder.Deliveries.Count == 1, "the next host on the broker delivers it");

        second.Recorder.Deliveries.Single().Context.MessageId.ShouldBe(id);
        broker.GetFailedAttempts(BusTestHost.Source, id.ToString()).ShouldBe(0);
        broker.GetDeadLetters(BusTestHost.Source).ShouldBeEmpty();
    }

    // ── Verification 6 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_WithATenant_PropagatesTenantCorrelationAndCausationThroughAHandlersPublication()
    {
        await using var host = await BusTestHost.StartAsync(bus => bus
            .AddHandler<ShippingOrderPlacedHandler>()
            .AddHandler<OrderShippedHandler>());

        var placedId = await host.PublishAsync(
            new OrderPlaced(Guid.NewGuid()), new PublishOptions { TenantId = "tenant-a" });

        await Eventually.HoldsAsync(() => host.Recorder.Deliveries.Count == 2, "both hops ran");

        var placed = host.Recorder.Deliveries.Single(d => d.Handler == nameof(ShippingOrderPlacedHandler));
        var shipped = host.Recorder.Deliveries.Single(d => d.Handler == nameof(OrderShippedHandler));

        placed.Context.TenantId.ShouldBe("tenant-a", "a tenant set on publish arrives in the context");

        shipped.Context.TenantId.ShouldBe("tenant-a", "the handler's publication inherits the tenant");
        shipped.Context.CorrelationId.ShouldBe(placed.Context.CorrelationId, "and the correlation");
        shipped.Context.CausationId.ShouldBe(
            placedId.ToString(), "and names the incoming message as its causation");
        shipped.Context.CausationId.ShouldNotBe(shipped.Context.CorrelationId);
    }

    // ── Verification 7 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScopeInitializer_RunsBeforeTheHandlersConstructorDependenciesAreCreated()
    {
        await using var host = await BusTestHost.StartAsync(
            bus => bus
                .AddScopeInitializer<TenantScopeInitializer>()
                .AddHandler<GatedOrderPlacedHandler>(),
            services: services => services
                .AddScoped<TenantSlot>()
                .AddScoped<TenantGate>());

        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()), new PublishOptions { TenantId = "tenant-a" });

        await Eventually.HoldsAsync(
            () => host.Recorder.Steps.Contains(nameof(GatedOrderPlacedHandler)), "the handler ran");

        host.Recorder.Steps.ShouldBe(
        [
            "initializer:tenant-a",
            "gate-constructed:tenant-a",
            nameof(GatedOrderPlacedHandler),
        ]);
    }
}

// ── Fixtures ───────────────────────────────────────────────────────────────────────────────────

/// <summary>Adds a header on send and records the step.</summary>
internal sealed class RecordingSendMiddleware(BusRecorder recorder) : IMessageSendMiddleware
{
    public ValueTask InvokeAsync(
        MessageSendContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        recorder.Step($"send:{context.WireType}");
        context.Headers["x-stamp"] = "stamped";
        return proceed();
    }
}

/// <summary>Records the step, with the header the send middleware added.</summary>
internal sealed class RecordingReceiveMiddleware(BusRecorder recorder) : IMessageReceiveMiddleware
{
    public ValueTask InvokeAsync(
        MessageReceiveContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        var stamp = context.Context.Headers.GetValueOrDefault("x-stamp", "none");
        recorder.Step($"receive:{context.HandlerType.Name}:{stamp}");
        return proceed();
    }
}

/// <summary>Signals that it started, then runs until its token is cancelled.</summary>
internal sealed class BlockingOrderPlacedHandler(TaskCompletionSource started) : IMessageHandler<OrderPlaced>
{
    public async ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }
}

/// <summary>Publishes a second message from inside the handler, through constructor injection.</summary>
internal sealed class ShippingOrderPlacedHandler(IMessageBus bus, BusRecorder recorder, ScopeMarker scope)
    : IMessageHandler<OrderPlaced>
{
    public async ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered(nameof(ShippingOrderPlacedHandler), message, context, scope.Id);
        await bus.PublishAsync(new OrderShipped(message.OrderId), ct: ct);
    }
}

/// <summary>Records <see cref="InvoiceIssued"/> deliveries.</summary>
internal sealed class InvoiceIssuedHandler(BusRecorder recorder, ScopeMarker scope) : IMessageHandler<InvoiceIssued>
{
    public ValueTask HandleAsync(InvoiceIssued message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered(nameof(InvoiceIssuedHandler), message, context, scope.Id);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Scoped state an initializer fills — the tenant a tenant-aware service reads.</summary>
internal sealed class TenantSlot
{
    public string? Tenant { get; set; }
}

/// <summary>Stands for a tenant-aware <c>DbContext</c>: reads the slot when constructed.</summary>
internal sealed class TenantGate
{
    public TenantGate(TenantSlot slot, BusRecorder recorder)
        => recorder.Step($"gate-constructed:{slot.Tenant ?? "none"}");
}

internal sealed class TenantScopeInitializer(BusRecorder recorder) : IMessageScopeInitializer
{
    public ValueTask InitializeAsync(
        MessageContext context, IServiceProvider services, CancellationToken ct = default)
    {
        services.GetRequiredService<TenantSlot>().Tenant = context.TenantId;
        recorder.Step($"initializer:{context.TenantId}");
        return ValueTask.CompletedTask;
    }
}

/// <summary>Takes the gate through its constructor, so the gate is built when the handler is.</summary>
internal sealed class GatedOrderPlacedHandler(TenantGate gate, BusRecorder recorder) : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        _ = gate;
        recorder.Step(nameof(GatedOrderPlacedHandler));
        return ValueTask.CompletedTask;
    }
}
