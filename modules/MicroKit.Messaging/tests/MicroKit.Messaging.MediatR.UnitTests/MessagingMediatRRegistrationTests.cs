using MicroKit.Messaging.MediatR.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// <see cref="MicroKit.Messaging.MediatR.DependencyInjection.MessagingMediatRExtensions.UseMediatRDomainEvents"/>:
/// what it registers, and what host start rejects (#188 D8, D12, C3).
/// </summary>
public sealed class MessagingMediatRRegistrationTests
{
    private static ServiceCollection Services(Action<IServiceCollection>? register = null, bool useTwice = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        register?.Invoke(services);
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.mediatr";
            bus.UseMediatRDomainEvents();
            if (useTwice)
            {
                bus.UseMediatRDomainEvents();
            }
        });
        return services;
    }

    /// <summary>What MicroKit.MediatR registers that the glue depends on, without scanning this assembly.</summary>
    private static void MicroKitMediatR(IServiceCollection services) => services
        .AddScoped(_ => Substitute.For<IDomainEventsDispatcher>())
        .AddScoped(_ => Substitute.For<IDomainEventNotificationFactory>());

    /// <summary>Runs every start-time check the host would run.</summary>
    private static async Task StartAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void CalledTwice_ContributesTheSinkTheMiddlewareAndTheSourceOnce()
    {
        var services = Services(MicroKitMediatR, useTwice: true);

        services.Count(d => d.ServiceType == typeof(IDomainEventsSink)).ShouldBe(1, "a second sink would send twice");
        services.Count(d => d.ServiceType == typeof(IMessageReceiveMiddleware)).ShouldBe(1, "a second one would dispatch twice");
        services.Count(d => d.ServiceType == typeof(NotificationHandlerSource)).ShouldBe(1);
    }

    [Fact]
    public void RegistersNoDomainEventsDispatcher_NoNotificationPublisher_AndNoSerializer()
    {
        // One orchestrator, MicroKit.MediatR's (ADR-MEDIATR-014); MediatR's own publisher is left
        // alone; the serializer is AddMicroKitMessaging's.
        var services = Services();
        var withoutGlue = new ServiceCollection();
        withoutGlue.AddMicroKitMessaging(bus => bus.ServiceName = "tests.mediatr");

        services.ShouldNotContain(d => d.ServiceType == typeof(IDomainEventsDispatcher));
        services.ShouldNotContain(d => d.ServiceType == typeof(INotificationPublisher));
        services.Count(d => d.ServiceType == typeof(IMessageSerializer))
            .ShouldBe(withoutGlue.Count(d => d.ServiceType == typeof(IMessageSerializer)));
    }

    [Fact]
    public async Task AtStart_WithoutMicroKitMediatR_FailsNamingTheFix()
    {
        var fault = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => StartAsync(Services()));

        fault.Message.ShouldContain("IDomainEventsDispatcher");
        fault.Message.ShouldContain("IDomainEventNotificationFactory");
        fault.Message.ShouldContain("AddMicroKitMediatR()");
    }

    [Fact]
    public async Task AtStart_WithMicroKitMediatR_AndClosedHandlers_Starts()
    {
        await StartAsync(Services(s =>
        {
            MicroKitMediatR(s);
            s.AddSingleton<CallLog>();
            s.AddTransient<INotificationHandler<OrderPlacedNotification>, FirstOrderHandler>();
        }));
    }

    [Fact]
    public async Task AtStart_AHandlerThatCannotBeAConsumer_FailsNamingIt_EvenWhenRegisteredAfterTheGlue()
    {
        // C3, end to end: the source is read once the container is built, so a registration made
        // after UseMediatRDomainEvents() is seen — and an open generic is reported, never skipped.
        var services = Services(MicroKitMediatR);
        services.AddTransient(typeof(INotificationHandler<>), typeof(OpenHandler<>));

        var fault = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => StartAsync(services));

        fault.Message.ShouldContain(typeof(OpenHandler<>).FullName!);
    }
}
