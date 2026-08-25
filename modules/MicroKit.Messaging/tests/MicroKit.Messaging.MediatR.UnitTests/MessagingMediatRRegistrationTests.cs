using MicroKit.Messaging.MediatR.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// The composition contract of <c>AddMediatRDomainEvents()</c>: it contributes exactly one
/// <see cref="IDomainEventsSink"/>, registers no rival <c>IDomainEventsDispatcher</c>, and takes the
/// <see cref="IOutboxDispatcher"/> seam in a way that no registration order can undo
/// (ADR-MEDIATR-014 / -015, ADR-MSG-019).
/// </summary>
/// <remarks>
/// <para>
/// Assertions resolve through a real <see cref="ServiceProvider"/> rather than inspecting
/// <see cref="ServiceDescriptor"/>s, and one goes further and dispatches an actual row. That last
/// layer is the one that matters: descriptor and resolved-type assertions prove shape, but only
/// driving a <see cref="MessageKind.Contract"/> row through to a recording transport proves the
/// decorator is still connected to its inner. The historical bypass — a second descriptor appended,
/// last one wins, no exception and no log — was a broken *chain*, not a wrong shape.
/// </para>
/// </remarks>
public sealed class MessagingMediatRRegistrationTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        // Dependencies no Add* method in this composition supplies: the two the outbox sink needs,
        // and the publisher the dispatcher needs.
        services.AddSingleton(Substitute.For<IDomainEventNotificationFactory>());
        services.AddScoped(_ => Substitute.For<IOutboxWriter>());
        services.AddSingleton(Substitute.For<IPublisher>());
        // Required by every test that RESOLVES the decorator while a transport dispatcher is
        // registered: the keyed inner is activated when the decorator is constructed, so a missing
        // IMessageTransport fails the whole resolution — notifications included. That is the
        // deliberate consequence recorded in ADR-MSG-019, and the two notification-only tests below
        // register no transport dispatcher at all rather than relying on this substitute.
        services.AddSingleton(Substitute.For<IMessageTransport>());
        // Open generic, not one closed ILogger<T> per activated type: AddMicroKitMessaging and the
        // dispatchers between them require several of these and nothing registers logging
        // (L0-FINDINGS.md Finding #1). A bare ServiceCollection has no ILogger<> at all.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    [Fact]
    public void AddMediatRDomainEvents_CalledTwice_ContributesTheSinkOnce()
    {
        var services = NewServices();

        services.AddMicroKitMessaging()
            .AddTransportDispatcher()
            .AddMediatRDomainEvents()
            .AddMediatRDomainEvents();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // TryAddEnumerable deduplicates on (ServiceType, ImplementationType). Under a plain Add the
        // sink would receive every batch twice and write every outbox row twice.
        scope.ServiceProvider.GetServices<IDomainEventsSink>().Count().ShouldBe(1);
    }

    [Fact]
    public void AddMediatRDomainEvents_CalledTwice_LeavesOneDispatcher()
    {
        // Previously guarded by a marker descriptor and asserted by nothing: a factory descriptor
        // carries no ImplementationType, so a second call would have found its OWN descriptor,
        // removed it and wrapped it again — double-routing every outbox message. Remove-then-add
        // makes that structural, and this is the test the marker never had.
        var services = NewServices();

        services.AddMicroKitMessaging()
            .AddTransportDispatcher()
            .AddMediatRDomainEvents()
            .AddMediatRDomainEvents();

        UnkeyedDispatchers(services).Count.ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>()
            .ShouldBeOfType<MediatROutboxDispatcher>();
    }

    [Fact]
    public void AddMediatRDomainEvents_RegistersNoDomainEventsDispatcher()
    {
        var services = NewServices();

        services.AddMicroKitMessaging()
            .AddTransportDispatcher()
            .AddMediatRDomainEvents();

        // ADR-MEDIATR-014: there is ONE IDomainEventsDispatcher implementation and it lives in
        // MicroKit.MediatR. This package contributes a sink and claims no dispatcher slot — so a
        // container without AddMicroKitMediatR() has no dispatcher at all.
        services.ShouldNotContain(d => d.ServiceType == typeof(IDomainEventsDispatcher));
    }

    [Fact]
    public void Registration_WithTransportFirst_ResolvesTheDecorator()
    {
        var services = NewServices();

        services.AddMicroKitMessaging()
            .AddTransportDispatcher()
            .AddMediatRDomainEvents();

        ResolveDispatcher(services).ShouldBeOfType<MediatROutboxDispatcher>();
    }

    [Fact]
    public void Registration_WithGlueFirst_ResolvesTheDecorator()
    {
        // The order that used to throw, because the glue had nothing to wrap. It is now legal: the
        // standard dispatcher lives in a keyed slot the glue never competes for, so
        // AddTransportDispatcher() can land afterwards and still be found.
        var services = NewServices();
        var builder = services.AddMicroKitMessaging();

        builder.AddMediatRDomainEvents();
        builder.AddTransportDispatcher();

        ResolveDispatcher(services).ShouldBeOfType<MediatROutboxDispatcher>();
    }

    [Fact]
    public void Registration_WithGlueFirst_StillRegistersTheKeyedStandardDispatcher()
    {
        // AddTransportDispatcher()'s unkeyed forwarder correctly abstains here — the decorator holds
        // that slot. What must NOT abstain with it is the keyed registration, or the decorator would
        // find no inner and every contract row would fail in a host that wired everything correctly.
        var services = NewServices();
        var builder = services.AddMicroKitMessaging();

        builder.AddMediatRDomainEvents();
        builder.AddTransportDispatcher();

        services.ShouldContain(d =>
            d.ServiceType == typeof(IOutboxDispatcher)
            && d.IsKeyedService
            && Equals(d.ServiceKey, OutboxDispatcherKeys.Standard));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_InEitherOrder_LeavesExactlyOneUnkeyedDispatcher(bool transportFirst)
    {
        var services = NewServices();
        var builder = services.AddMicroKitMessaging();

        if (transportFirst)
        {
            builder.AddTransportDispatcher();
            builder.AddMediatRDomainEvents();
        }
        else
        {
            builder.AddMediatRDomainEvents();
            builder.AddTransportDispatcher();
        }

        // A duplicate is what a built provider hides by resolving the last one, so it is asserted
        // against the collection rather than through the container.
        UnkeyedDispatchers(services).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registration_InEitherOrder_TheDecoratorActuallyReachesTheTransport(
        bool transportFirst)
    {
        // THE test. Everything above proves shape; this proves the chain. A decorator that resolves
        // but whose inner is null or disconnected passes every other assertion in this file and
        // silently strands every contract row — which is precisely the failure mode that shipped
        // once already.
        var transport = new RecordingTransport();
        var services = NewServices();
        services.AddSingleton<IMessageTransport>(transport);

        var builder = services.AddMicroKitMessaging();

        if (transportFirst)
        {
            builder.AddTransportDispatcher();
            builder.AddMediatRDomainEvents();
        }
        else
        {
            builder.AddMediatRDomainEvents();
            builder.AddTransportDispatcher();
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        await dispatcher.DispatchAsync(ContractRow());

        transport.Sent.ShouldHaveSingleItem().ContractName.ShouldBe("test.contract.v1");
    }

    [Fact]
    public async Task Registration_WithNoTransportDispatcher_ComposesAndDispatchesNotifications()
    {
        // The notification-only host of ADR-MSG-019: AddMediatRDomainEvents() alone, no transport
        // dispatcher, no IMessageTransport. This used to throw at registration.
        var services = NewServices();
        services.AddMicroKitMessaging().AddMediatRDomainEvents();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        dispatcher.ShouldBeOfType<MediatROutboxDispatcher>();

        // A notification row still dispatches. The serializer default comes from
        // AddMicroKitMessaging() — NOT from AddMediatRDomainEvents(), which registers none of its
        // own (ADR-MSG-019) — and returns null for an unresolvable EventType, which is a payload
        // fault rather than the configuration fault the next test asserts. Reaching
        // OutboxPayloadException is therefore proof the notification arm ran at all.
        await Should.ThrowAsync<OutboxPayloadException>(
            async () => await dispatcher.DispatchAsync(NotificationRow()));
    }

    [Fact]
    public void Registration_WithNoTransportDispatcher_StillResolvesTheSerializerDefault()
    {
        // The third composition of the serializer-ownership contract; the other three are pinned by
        // MessagingSerializerDefaultTests, which cannot reach the glue from the Core test project.
        //
        // Worth its own test rather than being left implied by the dispatch above: this method used
        // to TryAdd a serializer, and the notification arm is the ONLY path in this package that
        // needs one. If the default ever stops arriving from AddMicroKitMessaging(), a
        // notification-only host loses its dispatch path entirely — at the first worker tick, not
        // at composition.
        var services = NewServices();
        services.AddMicroKitMessaging().AddMediatRDomainEvents();

        using var provider = services.BuildServiceProvider();

        provider.GetService<IMessageSerializer>().ShouldNotBeNull();
    }

    [Fact]
    public void AddMediatRDomainEvents_AddsNoSerializerOfItsOwn()
    {
        // A DELTA, not an absence: AddMicroKitMessaging() has already supplied one. Asserting "no
        // serializer in the collection" would fail for the right reason and prove nothing.
        var services = NewServices();
        var builder = services.AddMicroKitMessaging();
        var before = services.Count(d => d.ServiceType == typeof(IMessageSerializer));

        builder.AddMediatRDomainEvents();

        services.Count(d => d.ServiceType == typeof(IMessageSerializer)).ShouldBe(before);
    }

    [Fact]
    public async Task Registration_WithNoTransportDispatcher_ContractRowIsAConfigurationFault()
    {
        var services = NewServices();
        services.AddMicroKitMessaging().AddMediatRDomainEvents();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        // Configuration, not payload: released, no retry budget spent, the row survives and drains
        // once a transport is deployed.
        var ex = await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await dispatcher.DispatchAsync(ContractRow()));

        ex.Message.ShouldContain("AddTransportDispatcher()");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static List<ServiceDescriptor> UnkeyedDispatchers(IServiceCollection services)
        => [.. services.Where(d =>
            d.ServiceType == typeof(IOutboxDispatcher) && !d.IsKeyedService)];

    private static IOutboxDispatcher ResolveDispatcher(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
    }

    private static OutboxMessage ContractRow() => new()
    {
        Id = MessageId.New(),
        MessageKind = MessageKind.Contract,
        ContractName = "test.contract.v1",
        Source = "/test",
        EventType = "SomeType",
        Payload = "{}",
        Status = OutboxMessageStatus.Pending,
        CorrelationId = CorrelationId.New(),
        OccurredOnUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static OutboxMessage NotificationRow() => new()
    {
        Id = MessageId.New(),
        MessageKind = MessageKind.Notification,
        EventType = "Unresolvable.Type, Nowhere",
        Payload = "{}",
        Status = OutboxMessageStatus.Pending,
        CorrelationId = CorrelationId.New(),
        OccurredOnUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private sealed class RecordingTransport : IMessageTransport
    {
        public List<MessageEnvelope> Sent { get; } = [];

        public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            Sent.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
