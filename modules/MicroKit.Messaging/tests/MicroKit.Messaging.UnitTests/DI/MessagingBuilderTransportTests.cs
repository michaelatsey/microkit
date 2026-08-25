namespace MicroKit.Messaging.UnitTests.DI;

using MicroKit.Messaging.Dispatch;
using Microsoft.Extensions.Logging;

/// <summary>
/// Composition properties of <see cref="MessagingBuilder.AddTransportDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Asserted against the <see cref="IServiceCollection"/> descriptors rather than a built provider:
/// what matters here is <i>how many</i> descriptors exist and with what lifetime, and a built
/// provider hides a duplicate by resolving the last one — which is precisely the failure these
/// tests exist to catch.
/// </para>
/// <para>
/// <b>That discipline alone stopped being sufficient</b> when the method grew a second registration
/// (ADR-MSG-019). The dispatcher now lives in a keyed slot, and the unkeyed
/// <see cref="IOutboxDispatcher"/> is a <i>factory</i> forwarder whose
/// <see cref="ServiceDescriptor.ImplementationType"/> is null — so a descriptor assertion can no
/// longer see what the unkeyed slot resolves to. Both layers are therefore kept: descriptors for
/// counting, and one resolution test for what the forwarder actually yields.
/// </para>
/// </remarks>
public sealed class MessagingBuilderTransportTests
{
    private static MessagingBuilder Builder(IServiceCollection services)
        => services.AddMicroKitMessaging();

    private static ServiceDescriptor KeyedDispatcher(IServiceCollection services)
        => services
            .Where(d => d.ServiceType == typeof(IOutboxDispatcher) && d.IsKeyedService)
            .ShouldHaveSingleItem();

    private static List<ServiceDescriptor> UnkeyedDispatchers(IServiceCollection services)
        => [.. services.Where(d =>
            d.ServiceType == typeof(IOutboxDispatcher) && !d.IsKeyedService)];

    [Fact]
    public void AddTransportDispatcher_RegistersTheDispatcherKeyedAndScoped()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher();

        var descriptor = KeyedDispatcher(services);
        // KeyedImplementationType, not ImplementationType: the unkeyed property is not the one a
        // keyed descriptor populates, and reading the wrong one yields null rather than failing.
        descriptor.KeyedImplementationType.ShouldBe(typeof(TransportOutboxDispatcher));
        descriptor.ServiceKey.ShouldBe(OutboxDispatcherKeys.Standard);

        // Scoped, not singleton: it is resolved from the per-message execution scope, and a
        // singleton would capture whatever scoped dependencies a transport brings with it.
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddTransportDispatcher_RegistersOneUnkeyedForwarder()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher();

        var forwarder = UnkeyedDispatchers(services).ShouldHaveSingleItem();
        forwarder.Lifetime.ShouldBe(ServiceLifetime.Scoped);

        // A factory, so ImplementationType is null and no descriptor assertion can tell what it
        // resolves to. That is what the next test is for.
        forwarder.ImplementationType.ShouldBeNull();
    }

    [Fact]
    public void AddTransportDispatcher_TheUnkeyedSeamResolvesToTheTransportDispatcher()
    {
        // Core alone: every row goes to the transport. The forwarder is the only thing making that
        // true, and it is invisible to the descriptor assertions above.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        Builder(services).AddTransportDispatcher();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>()
            .ShouldBeOfType<TransportOutboxDispatcher>();
    }

    /// <summary>
    /// It registers no transport, and that is the design rather than an omission.
    /// </summary>
    /// <remarks>
    /// No <see cref="IMessageTransport"/> implementation ships anywhere in MicroKit: an in-process
    /// one with no receiver could only either succeed silently — marking rows <c>Published</c> that
    /// were never delivered — or exist purely to throw. If this assertion ever fails, something has
    /// shipped that should not have.
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
        // The transport path carries the payload opaque and never deserializes it, so this method
        // has no business registering an IMessageSerializer. Asserted as a DELTA rather than an
        // absence: AddMicroKitMessaging() supplies the default — it owns InboxProcessor and
        // OutboxMessageFactory, which both require one — so "no serializer in the collection" would
        // be asserting the wrong thing and would fail for the right reason.
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

        UnkeyedDispatchers(services).Count.ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IOutboxDispatcher) && d.IsKeyedService)
            .ShouldBe(1);
    }

    [Fact]
    public void AddTransportDispatcher_DoesNotDisplaceAnExistingUnkeyedDispatcher()
    {
        // A decorating package takes the unkeyed seam outright. Calling this method afterwards must
        // leave it alone — the direction that had shipped as a silent bypass under a plain Add
        // (ADR-MSG-016), and the direction that then broke the other way when TryAdd made the whole
        // method abstain. The keyed half must still land; see the next test.
        var services = new ServiceCollection();
        var builder = Builder(services);
        services.AddScoped<IOutboxDispatcher, StubDispatcher>();

        builder.AddTransportDispatcher();

        UnkeyedDispatchers(services).ShouldHaveSingleItem()
            .ImplementationType.ShouldBe(typeof(StubDispatcher));
    }

    [Fact]
    public void AddTransportDispatcher_StillRegistersTheKeyedSlotWhenTheSeamIsTaken()
    {
        // The half that must NOT abstain with the forwarder. Without it a decorator registered
        // first would find no inner, and every contract row would fail as a configuration fault in
        // a host that had wired everything correctly — just in the other order.
        var services = new ServiceCollection();
        var builder = Builder(services);
        services.AddScoped<IOutboxDispatcher, StubDispatcher>();

        builder.AddTransportDispatcher();

        KeyedDispatcher(services).KeyedImplementationType.ShouldBe(typeof(TransportOutboxDispatcher));
    }

    private sealed class StubDispatcher : IOutboxDispatcher
    {
        public ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
