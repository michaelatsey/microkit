using MicroKit.MediatR.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Events;

/// <summary>
/// Verifies that <see cref="IDomainEventHandlerDispatcher"/> invokes registered
/// <see cref="IDomainEventHandler{TEvent}"/> implementations directly, without going
/// through the MediatR notification pipeline.
/// </summary>
public sealed class DomainEventHandlerDispatcherTests
{
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DomainEventLog());
        services.AddMicroKitMediatR(cfg => cfg.FromAssemblyContaining<EchoCommand>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerRegistered_InvokesHandlerWithRawEvent()
    {
        using var provider = BuildContainer();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventHandlerDispatcher>();
        var log = provider.GetRequiredService<DomainEventLog>();
        var itemId = Guid.NewGuid();

        await dispatcher.DispatchAsync(new ItemCreatedEvent(itemId));

        log.ItemCreatedIds.Count.ShouldBe(1);
        log.ItemCreatedIds[0].ShouldBe(itemId);
    }

    [Fact]
    public async Task DispatchAsync_WhenMultipleHandlersForSameEvent_InvokesAllSequentially()
    {
        using var provider = BuildContainer();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventHandlerDispatcher>();
        var log = provider.GetRequiredService<DomainEventLog>();

        await dispatcher.DispatchAsync(new OrderPlacedEvent(Guid.NewGuid()));

        log.OrderPlacedInvocations.ShouldBe(2);
    }

    [Fact]
    public async Task DispatchAsync_WhenNoHandlerRegistered_DoesNotThrow()
    {
        using var provider = BuildContainer();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventHandlerDispatcher>();

        await Should.NotThrowAsync(async () =>
            await dispatcher.DispatchAsync(new UnregisteredEvent(Guid.NewGuid())));
    }
}
