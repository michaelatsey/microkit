using Microsoft.Extensions.Time.Testing;
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
    public void AStore_RegistersBothDispatchers_WhateverTheTransport()
    {
        // A local row is drained by its store's own loop (#188 D7) whatever the transport; a
        // transport row goes to whatever IMessageTransport the host registers.
        var services = new ServiceCollection();

        Builder(services).AddMessageStore(new MessageStoreKey("tests:local"), _ => { });

        Dispatchers(services, OutboxDispatchers.Local).ShouldHaveSingleItem()
            .KeyedImplementationType.ShouldBe(typeof(LocalOutboxDispatcher));
        // A factory: ValidateOnBuild must accept a store with no transport (next test).
        Dispatchers(services, OutboxDispatchers.Transport).ShouldHaveSingleItem()
            .KeyedImplementationFactory.ShouldNotBeNull();
    }

    [Fact]
    public void AStoreWithNoTransport_PassesValidateOnBuild()
    {
        // Review finding (#188 P4): a typed registration of the transport dispatcher made
        // Host.CreateApplicationBuilder() fail at Build() in Development (ValidateOnBuild) for the
        // README's own composition — a store, domain events, no transport.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        Builder(services).AddMessageStore(new MessageStoreKey("tests:validated"), _ => { });

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task AStoreWithAProvidersOwnTransport_SendsItsTransportRowsThroughIt()
    {
        // A broker provider registers its IMessageTransport and nothing else: it has no access to
        // the internal dispatcher. The store's registration is what lets its rows leave.
        var transport = new RecordingMessageTransport();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IMessageTransport>(transport);
        Builder(services).AddMessageStore(new MessageStoreKey("tests:provider"), _ => { });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var row = OutboxFixtures.ContractMessage();
        await scope.ServiceProvider.GetRequiredKeyedService<IOutboxDispatcher>(OutboxDispatchers.Transport).DispatchAsync(row);

        transport.Sent.ShouldHaveSingleItem().Id.ShouldBe(row.Id.Value.ToString());
    }

    [Fact]
    public async Task AStoreWithNoTransport_CannotActivateTheTransportDispatcher()
    {
        // The reversible verdict (event 1009, back-off): the container cannot build the dispatcher,
        // which OutboxProcessor tags as an activation failure rather than a missing registration.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        Builder(services).AddMessageStore(new MessageStoreKey("tests:none"), _ => { });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Should.Throw<InvalidOperationException>(
            () => scope.ServiceProvider.GetKeyedService<IOutboxDispatcher>(OutboxDispatchers.Transport));
    }

    [Fact]
    public async Task AStoresProcessor_KeepsItsEndpointBackOffAcrossCycles()
    {
        // The processor is scoped — rebuilt every cycle — and the back-off must outlive it, or a down
        // endpoint is tried again on every cycle (#188). Two cycles, no transport: one activation
        // failure, then the endpoint's rows are deferred without a second attempt.
        var key = new MessageStoreKey("tests:backoff");
        var row = OutboxFixtures.ContractMessage();
        var store = Substitute.For<IOutboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult(OutboxFixtures.Claim(row)));
        var logs = new CapturingLogger<OutboxProcessor>();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<OutboxProcessor>>(logs);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        var bus = Builder(services);
        bus.ServiceName = "tests.backoff";
        bus.AddMessageStore(key, s => s.Services.AddKeyedScoped(key, (_, _) => store));

        await using var provider = services.BuildServiceProvider();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(key).ProcessBatchAsync(10);
        }

        logs.Records.Count(r => r.EventId == 1009).ShouldBe(1, "the second cycle found the endpoint in back-off");
        logs.Records.Single(r => r.EventId == 1009).Exception!.ToString()
            .ShouldContain("bus.UseInMemoryTransport()", Case.Sensitive, "the activation fault names the fix");
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
