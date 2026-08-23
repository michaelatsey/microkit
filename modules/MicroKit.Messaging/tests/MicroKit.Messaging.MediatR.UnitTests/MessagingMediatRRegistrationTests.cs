using MicroKit.Messaging.MediatR.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// The composition contract of <c>AddMediatRDomainEvents()</c>: it contributes exactly one
/// <see cref="IDomainEventsSink"/>, registers no rival <c>IDomainEventsDispatcher</c>, and survives
/// being called twice or having a transport registered after it (ADR-MEDIATR-014 / -015).
/// </summary>
/// <remarks>
/// Assertions resolve through a real <see cref="ServiceProvider"/> rather than inspecting
/// <see cref="ServiceDescriptor"/>s. The sink's own collaborators are substituted because this
/// project deliberately does not compose a database.
/// </remarks>
public sealed class MessagingMediatRRegistrationTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        // Dependencies no Add* method in this composition supplies: the two the outbox sink needs,
        // the store the in-process publisher needs, and the logger the decorator needs.
        services.AddSingleton(Substitute.For<IDomainEventNotificationFactory>());
        services.AddScoped(_ => Substitute.For<IOutboxWriter>());
        services.AddScoped(_ => Substitute.For<IInboxWriter>());
        services.AddSingleton(Substitute.For<IPublisher>());
        // Open generic, not one closed ILogger<T> per activated type: AddMicroKitMessaging and the
        // transports between them require six of these and nothing registers logging
        // (L0-FINDINGS.md Finding #1). A bare ServiceCollection has no ILogger<> at all.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    [Fact]
    public void AddMediatRDomainEvents_CalledTwice_ContributesTheSinkOnce()
    {
        var services = NewServices();

        services.AddMicroKitMessaging()
            .AddInProcessTransport()
            .AddMediatRDomainEvents()
            .AddMediatRDomainEvents();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // TryAddEnumerable deduplicates on (ServiceType, ImplementationType). Under a plain Add the
        // sink would receive every batch twice and write every outbox row twice.
        scope.ServiceProvider.GetServices<IDomainEventsSink>().Count().ShouldBe(1);
    }

    [Fact]
    public void AddMediatRDomainEvents_RegistersNoDomainEventsDispatcher()
    {
        var services = NewServices();

        services.AddMicroKitMessaging()
            .AddInProcessTransport()
            .AddMediatRDomainEvents();

        // ADR-MEDIATR-014: there is ONE IDomainEventsDispatcher implementation and it lives in
        // MicroKit.MediatR. This package contributes a sink and claims no dispatcher slot — so a
        // container without AddMicroKitMediatR() has no dispatcher at all.
        services.ShouldNotContain(d => d.ServiceType == typeof(IDomainEventsDispatcher));
    }

    [Fact]
    public void AddInProcessTransport_CalledAfterTheGlue_KeepsTheDecorator()
    {
        var services = NewServices();

        var builder = services.AddMicroKitMessaging().AddInProcessTransport();
        builder.AddMediatRDomainEvents();

        // The silent failure this guards: under a plain Add this appended a second
        // IOutboxDispatcher descriptor, Microsoft DI resolved the last one, and
        // MediatROutboxDispatcher was bypassed with no exception and no log.
        builder.AddInProcessTransport();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>()
            .ShouldBeOfType<MediatROutboxDispatcher>();
    }

    [Fact]
    public void AddMediatRDomainEvents_WhenNoTransportRegistered_ThrowsNamingTheFix()
    {
        var services = NewServices();
        var builder = services.AddMicroKitMessaging();

        // Loud, not silent: there is no IOutboxDispatcher to decorate.
        var ex = Should.Throw<InvalidOperationException>(() => builder.AddMediatRDomainEvents());

        ex.Message.ShouldContain("IOutboxDispatcher");
        ex.Message.ShouldContain("AddInProcessTransport()");
    }
}
