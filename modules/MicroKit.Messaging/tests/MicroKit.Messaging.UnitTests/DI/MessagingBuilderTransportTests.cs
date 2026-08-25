namespace MicroKit.Messaging.UnitTests.DI;

using MicroKit.Messaging.Dispatch;

/// <summary>
/// Composition properties of <see cref="MessagingBuilder.AddTransportDispatcher"/>.
/// </summary>
/// <remarks>
/// Asserted against the <see cref="IServiceCollection"/> descriptors rather than a built provider:
/// what matters here is <i>how many</i> descriptors exist and with what lifetime, and a built
/// provider hides a duplicate by resolving the last one — which is precisely the failure these
/// tests exist to catch.
/// </remarks>
public sealed class MessagingBuilderTransportTests
{
    private static MessagingBuilder Builder(IServiceCollection services)
        => services.AddMicroKitMessaging();

    [Fact]
    public void AddTransportDispatcher_RegistersTheDispatcherScoped()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher();

        var descriptor = services
            .Where(d => d.ServiceType == typeof(IOutboxDispatcher))
            .ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(TransportOutboxDispatcher));

        // Scoped, not singleton: it is resolved from the per-message execution scope, and a
        // singleton would capture whatever scoped dependencies a transport brings with it.
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
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
    public void AddTransportDispatcher_IsIdempotent()
    {
        var services = new ServiceCollection();

        Builder(services).AddTransportDispatcher().AddTransportDispatcher();

        services.Count(d => d.ServiceType == typeof(IOutboxDispatcher)).ShouldBe(1);
    }

    /// <summary>
    /// It abstains when something already holds the <see cref="IOutboxDispatcher"/> slot.
    /// </summary>
    /// <remarks>
    /// This is the <c>TryAdd</c> property, and it is not a nicety. Under a plain <c>Add</c> a second
    /// descriptor is appended, Microsoft DI resolves the <i>last</i> one, and a decorator registered
    /// earlier — the MediatR routing dispatcher, for instance — is bypassed with no exception and no
    /// log: the outbox keeps draining while nothing it routes is ever delivered. The same defect
    /// was found and fixed on <see cref="MessagingBuilder.AddInProcessTransport"/>.
    /// </remarks>
    [Fact]
    public void AddTransportDispatcher_DoesNotDisplaceAnExistingDispatcher()
    {
        var services = new ServiceCollection();
        var builder = Builder(services);
        builder.AddInProcessTransport();

        builder.AddTransportDispatcher();

        var descriptor = services
            .Where(d => d.ServiceType == typeof(IOutboxDispatcher))
            .ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(InProcessIntegrationDispatcher));
    }

    /// <summary>The two dispatchers are interchangeable at the seam, in either composition order.</summary>
    [Fact]
    public void AddInProcessTransport_DoesNotDisplaceTheTransportDispatcher()
    {
        var services = new ServiceCollection();
        var builder = Builder(services);
        builder.AddTransportDispatcher();

        builder.AddInProcessTransport();

        var descriptor = services
            .Where(d => d.ServiceType == typeof(IOutboxDispatcher))
            .ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(TransportOutboxDispatcher));
    }
}
