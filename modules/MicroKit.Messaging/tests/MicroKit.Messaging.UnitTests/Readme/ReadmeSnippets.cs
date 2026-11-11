// The C# blocks of the module README that use MicroKit.Messaging alone, compiled here (#162,
// #187). Every README block marked <!-- snippet: name --> is the text between
// "// begin-snippet: name" and "// end-snippet" in this file or in
// tests/MicroKit.Messaging.MediatR.IntegrationTests/Readme/ReadmeSnippets.cs, and
// ReadmeSnippetTests fails when the two differ. Edit both together.
#pragma warning disable CA1050, CA1062, CA1848, CA1873, IDE0060 // README code favours the plain, idiomatic call
namespace MicroKit.Messaging.UnitTests.Readme;

using Microsoft.Extensions.Hosting;

// begin-snippet: quick-start-contracts
[MessageContract("shop.orders.order-placed")]   // wire type: shop.orders.order-placed.v1
public sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        logger.LogInformation("Order {OrderId} placed ({MessageId})", message.OrderId, context.MessageId);
        return ValueTask.CompletedTask;
    }
}
// end-snippet

// begin-snippet: quick-start-publish
public sealed class Checkout(IMessageBus bus)
{
    public async Task PlaceAsync(Guid orderId, CancellationToken ct = default)
        => await bus.PublishAsync(new OrderPlaced(orderId), ct: ct);
}
// end-snippet

/// <summary>The shipping service's own type for the contract the orders service publishes.</summary>
public static class Shipping
{
    // begin-snippet: consuming-contract
    [MessageContract("shop.orders.order-placed")]   // the producer's name, on this service's own type
    public sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

    public sealed class ReserveStock : IMessageHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
    // end-snippet
}

internal static class ReadmeSnippets
{
    public static void QuickStart(IServiceCollection services)
    {
        // begin-snippet: quick-start
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.orders";
            bus.Publishes<OrderPlaced>();
            bus.AddHandler<OrderPlacedHandler>();
            bus.UseInMemoryTransport();
        });
        // end-snippet
    }

    public static void ConsumingComposition(IServiceCollection services)
    {
        // begin-snippet: consuming-composition
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.shipping";
            bus.AddHandler<Shipping.ReserveStock>();   // a handler is its subscription
            bus.UseInMemoryTransport();
        });
        // end-snippet
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Mirrors the README; never executed.")]
    public static void HandlerDiscovery(IServiceCollection services)
    {
        // begin-snippet: handler-discovery
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.shipping";
            bus.AddHandlersFromAssemblyContaining<OrderPlacedHandler>();   // reflection: not trim-safe
            bus.UseInMemoryTransport();
        });
        // end-snippet
    }

    public static void Options(IServiceCollection services)
    {
        // begin-snippet: options
        services.AddMicroKitMessaging(bus =>
        {
            bus.Configure(options =>
            {
                options.Outbox.BatchSize = 200;
                options.Inbox.LeaseDuration = TimeSpan.FromMinutes(10);
            });
        });
        // end-snippet
    }

    public static async Task EndToEndAsync(string[] args)
    {
        // begin-snippet: end-to-end
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.orders";
            bus.Publishes<OrderPlaced>();
            bus.AddHandler<OrderPlacedHandler>();
            bus.UseInMemoryTransport();
        });

        using var host = builder.Build();
        await host.StartAsync();   // validates the options and the composition, then binds the endpoint

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var checkout = ActivatorUtilities.CreateInstance<Checkout>(scope.ServiceProvider);
            await checkout.PlaceAsync(Guid.NewGuid());   // OrderPlacedHandler logs it, in its own scope
        }

        await host.StopAsync();
        // end-snippet
    }
}
