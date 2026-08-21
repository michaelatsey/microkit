using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Registration;

/// <summary>
/// Verifies the MicroKit.MediatR half of the ADR-MEDIATR-013 registration-precedence contract:
/// <c>AddMicroKitMediatR</c> supplies <see cref="IDomainEventsDispatcher"/> as a <b>default</b>
/// (TryAdd) — it does not impose one. Every assertion resolves through a real
/// <see cref="ServiceProvider"/>; nothing here inspects <see cref="ServiceDescriptor"/>s.
/// </summary>
public sealed class DomainEventsDispatcherRegistrationTests
{
    // The core implementation is internal and the module ships no InternalsVisibleTo, so it is
    // located by name off the core assembly rather than referenced directly. Adding an
    // InternalsVisibleTo purely for a test would widen a shipped package's surface.
    private static readonly Type CoreDispatcherType =
        typeof(IDomainEventsDispatcher).Assembly
            .GetType("MicroKit.MediatR.Events.DomainEventDispatcher", throwOnError: true)!;

    [Fact]
    public void AddMicroKitMediatR_WhenCalledAlone_ResolvesCoreDispatcher()
    {
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>();

        dispatcher.ShouldBeOfType(CoreDispatcherType);
    }

    [Fact]
    public void AddMicroKitMediatR_WhenDispatcherAlreadyRegistered_KeepsExistingRegistration()
    {
        // Stands in for AddMediatRTransport(): MicroKit.MediatR cannot reference MicroKit.Messaging
        // (that edge is inverted in the dependency graph), so a prior registration is the faithful
        // simulation of the glue-then-core call order. Under plain Add this resolved to the core
        // dispatcher and every outbox write was silently lost.
        var services = NewServices();
        services.AddScoped<IDomainEventsDispatcher, SupersedingDispatcher>();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>();

        dispatcher.ShouldBeOfType<SupersedingDispatcher>();
    }

    [Fact]
    public void AddMicroKitMediatR_WhenCalledTwice_RegistersDispatcherOnce()
    {
        // Scanning a handler-free assembly isolates the dispatcher descriptors: a second scan of an
        // assembly that DOES declare handlers would also re-register the handler map and the
        // notification factory (a separate, unrelated defect — last-wins on those singletons).
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);
        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // All three dispatcher descriptors are asserted: the rule has no exceptions, so neither does
        // its test. Each assertion fails independently if its own registration reverts to Add.
        scope.ServiceProvider.GetServices(CoreDispatcherType).Count().ShouldBe(1);
        scope.ServiceProvider.GetServices<IDomainEventsDispatcher>().Count().ShouldBe(1);
#pragma warning disable CS0618 // The [Obsolete] alias is deliberately under test — the rule has no exceptions.
        scope.ServiceProvider.GetServices<IDomainEventDispatcher>().Count().ShouldBe(1);
#pragma warning restore CS0618
    }

    [Fact]
    public void AddMicroKitMediatR_WhenCalledAlone_ConcreteDispatcherStillResolves()
    {
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(CoreDispatcherType).ShouldNotBeNull();
    }

    [Fact]
    public void AddMicroKitMediatR_WhenDispatcherAlreadyRegistered_ConcreteDispatcherStillResolves()
    {
        // The concrete descriptor is registered unconditionally — it must not be skipped merely
        // because the interface slot was already taken. In the glue-then-core order it is inert.
        var services = NewServices();
        services.AddScoped<IDomainEventsDispatcher, SupersedingDispatcher>();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(CoreDispatcherType).ShouldNotBeNull();
    }

    // IDomainEventsProvider is the one dependency of the core dispatcher that AddMicroKitMediatR
    // does not register itself — it comes from the consumer's persistence layer.
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDomainEventsProvider>());
        return services;
    }

    // AddMicroKitMediatR requires at least one assembly — MediatR throws "No assemblies found to
    // scan" otherwise. The MicroKit.MediatR core assembly declares no handlers and no notifications,
    // so scanning it registers nothing that could interfere with the dispatcher descriptors under
    // test, and a second AddMicroKitMediatR call adds no handler registrations either.
    private static void ScanAssemblyWithoutHandlers(MediatRBuilder builder)
        => builder.FromAssembly(typeof(IDomainEventsDispatcher).Assembly);

    // Stands in for MicroKit.Messaging.MediatR's four-phase DomainEventsDispatcher.
    private sealed class SupersedingDispatcher : IDomainEventsDispatcher
    {
        public Task DispatchEventsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
