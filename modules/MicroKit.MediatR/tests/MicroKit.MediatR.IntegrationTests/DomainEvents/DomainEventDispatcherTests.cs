using MicroKit.MediatR;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.DomainEvents;

/// <summary>
/// Verifies <see cref="IDomainEventDispatcher"/> end-to-end behaviour:
/// single-handler dispatch, multi-handler fan-out, and the dispatch-time error
/// when no notification is registered for an event type (ADR-005).
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private static ServiceProvider BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var log = new DomainEventLog();
        services.AddSingleton(log);
        services.AddMicroKitMediatR(cfg => cfg.FromAssemblyContaining<ItemCreatedEvent>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishAsync_WhenSingleHandlerRegistered_HandlerIsInvoked()
    {
        using var provider = BuildPipeline();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        var log = provider.GetRequiredService<DomainEventLog>();
        var id = Guid.NewGuid();

        await dispatcher.PublishAsync(new ItemCreatedEvent(id));

        log.ItemCreatedIds.Count.ShouldBe(1);
        log.ItemCreatedIds[0].ShouldBe(id);
    }

    [Fact]
    public async Task PublishAsync_WhenTwoHandlersForSameEvent_BothHandlersAreInvoked()
    {
        // OrderPlacedHandlerOne and OrderPlacedHandlerTwo both handle OrderPlacedEvent
        // with the same notification type — MediatR fan-out pattern (ADR-005).
        using var provider = BuildPipeline();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        var log = provider.GetRequiredService<DomainEventLog>();

        await dispatcher.PublishAsync(new OrderPlacedEvent(Guid.NewGuid()));

        log.OrderPlacedInvocations.ShouldBe(2,
            "both registered handlers must be invoked for the same notification type");
    }

    [Fact]
    public async Task PublishAsync_WhenNoHandlerForEventType_ThrowsInvalidOperationExceptionAtDispatchTime()
    {
        // ADR-005: missing handler coverage is not detected at DI startup —
        // it surfaces only when IDomainEventDispatcher.PublishAsync is first called.
        using var provider = BuildPipeline();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await dispatcher.PublishAsync(new UnregisteredEvent(Guid.NewGuid())));

        ex.Message.ShouldContain(nameof(UnregisteredEvent));
    }
}
