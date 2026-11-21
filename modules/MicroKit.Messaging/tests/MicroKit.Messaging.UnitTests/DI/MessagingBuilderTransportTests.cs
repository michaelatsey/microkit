namespace MicroKit.Messaging.UnitTests.DI;

using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Local;

/// <summary>
/// How an outbox row reaches its dispatcher (#188 D7): one dispatcher per endpoint, registered
/// under its key, and the row's <see cref="OutboxMessage.Endpoint"/> naming the key.
/// </summary>
/// <remarks>
/// Asserted against the <see cref="IServiceCollection"/> descriptors where the count and lifetime
/// matter — a built provider hides a duplicate by resolving the last one — and by resolution where
/// what a key yields matters.
/// </remarks>
public sealed class MessagingBuilderTransportTests
{
    private static MessagingBuilder Builder(IServiceCollection services)
        => services.AddMicroKitMessaging(_ => { });

    private static List<ServiceDescriptor> Dispatchers(IServiceCollection services, string key)
        => [.. services.Where(d =>
            d.ServiceType == typeof(IOutboxDispatcher) && d.IsKeyedService && Equals(d.ServiceKey, key))];

    [Fact]
    public void AddTransportDispatcher_RegistersTheDispatcherUnderTheTransportKey_Scoped()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher();

        var descriptor = Dispatchers(services, OutboxDispatchers.Transport).ShouldHaveSingleItem();
        // KeyedImplementationType, not ImplementationType: the unkeyed property is not the one a
        // keyed descriptor populates, and reading the wrong one yields null rather than failing.
        descriptor.KeyedImplementationType.ShouldBe(typeof(TransportOutboxDispatcher));
        // Scoped: resolved from the per-message execution scope; a singleton would capture whatever
        // scoped dependencies a transport brings with it.
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddTransportDispatcher_RegistersNoUnkeyedDispatcher()
    {
        // The engine resolves by the row's endpoint only: an unkeyed slot would be a second, silent
        // route that nothing reads.
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher();

        services.ShouldNotContain(d => d.ServiceType == typeof(IOutboxDispatcher) && !d.IsKeyedService);
    }

    [Fact]
    public void TheTransportKey_ResolvesToTheTransportDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        Builder(services).AddTransportDispatcher();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IOutboxDispatcher>(OutboxDispatchers.Transport)
            .ShouldBeOfType<TransportOutboxDispatcher>();
    }

    [Fact]
    public void AStore_RegistersTheLocalDispatcher_WhateverTheTransport()
    {
        // A local row is drained by its store's own loop (#188 D7): with no transport registered at
        // all, the local dispatcher is still there.
        var services = new ServiceCollection();

        Builder(services).AddMessageStore(new MessageStoreKey("tests:local"), _ => { });

        Dispatchers(services, OutboxDispatchers.Local).ShouldHaveSingleItem()
            .KeyedImplementationType.ShouldBe(typeof(LocalOutboxDispatcher));
        Dispatchers(services, OutboxDispatchers.Transport).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, OutboxDispatchers.Transport)]
    [InlineData(OutboxMessage.LocalEndpoint, OutboxDispatchers.Local)]
    [InlineData("rabbitmq", null)]
    [InlineData("LOCAL", null)]
    public void TheRowsEndpoint_NamesItsDispatcher(string? endpoint, string? expectedKey)
    {
        // An endpoint this build cannot route has no key: OutboxProcessor dead-letters the row
        // rather than guess (ordinal: "LOCAL" is not the local endpoint).
        var row = new OutboxMessage { Endpoint = endpoint };

        OutboxDispatchers.KeyOf(row).ShouldBe(expectedKey);
    }

    /// <summary>
    /// It registers no transport, and that is the design rather than an omission.
    /// </summary>
    /// <remarks>
    /// No <see cref="IMessageTransport"/> implementation ships with this method: an in-process one
    /// with no receiver could only either succeed silently — marking rows <c>Published</c> that were
    /// never delivered — or exist purely to throw.
    /// </remarks>
    [Fact]
    public void AddTransportDispatcher_RegistersNoTransport()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher();

        services.ShouldNotContain(d => d.ServiceType == typeof(IMessageTransport));
    }

    [Fact]
    public void AddTransportDispatcher_AddsNoSerializerOfItsOwn()
    {
        // The transport path carries the payload opaque and never deserializes it. Asserted as a
        // DELTA: AddMicroKitMessaging() supplies the default serializer.
        var services = new ServiceCollection();
        var builder = Builder(services);
        var before = services.Count(d => d.ServiceType == typeof(IMessageSerializer));

        builder.AddTransportDispatcher();

        services.Count(d => d.ServiceType == typeof(IMessageSerializer)).ShouldBe(before);
    }

    [Fact]
    public void AddTransportDispatcher_IsIdempotent()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher().AddTransportDispatcher();

        Dispatchers(services, OutboxDispatchers.Transport).Count.ShouldBe(1);
    }
}
