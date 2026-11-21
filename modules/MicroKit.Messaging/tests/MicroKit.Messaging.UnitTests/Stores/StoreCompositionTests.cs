using Microsoft.Extensions.Hosting;

namespace MicroKit.Messaging.UnitTests.Stores;

/// <summary>
/// D12 (#188), the part the composition analysis owns: an assembly bound to two stores, and a
/// handler bound to none while several stores are configured, fail at start with their fix. The
/// engine is registered once, whatever the number of stores and calls.
/// </summary>
public sealed class StoreCompositionTests
{
    private static readonly MessageStoreKey A = new("tests:a");
    private static readonly MessageStoreKey B = new("tests:b");

    [Fact]
    public void AnAssemblyBoundToTwoStores_IsAProblem()
    {
        var problems = Analyze(bus =>
        {
            bus.AddMessageStore(A, s => s.BindAssembly(typeof(StoreCompositionTests).Assembly));
            bus.AddMessageStore(B, s => s.BindAssembly(typeof(StoreCompositionTests).Assembly));
        });

        problems.ShouldHaveSingleItem().ShouldContain(
            "Assembly 'MicroKit.Messaging.UnitTests' is bound to 2 stores ('tests:a', 'tests:b')");
    }

    [Fact]
    public void WithSeveralStores_AnUnboundHandler_IsAProblemNamingTheFix()
    {
        var problems = Analyze(bus =>
        {
            bus.AddHandler<StoreBoundHandler>();
            bus.AddMessageStore(A, _ => { });
            bus.AddMessageStore(B, _ => { });
        });

        var problem = problems.ShouldHaveSingleItem();
        problem.ShouldContain($"Handler '{typeof(StoreBoundHandler).FullName}' is bound to no store");
        problem.ShouldContain("store.ForAssemblyContaining<StoreBoundHandler>()");
    }

    [Fact]
    public void WithSeveralStores_ABoundHandler_IsFine()
        => Analyze(bus =>
        {
            bus.AddHandler<StoreBoundHandler>();
            bus.AddMessageStore(A, s => s.BindAssembly(typeof(StoreBoundHandler).Assembly));
            bus.AddMessageStore(B, _ => { });
        }).ShouldBeEmpty();

    [Fact]
    public void WithOneStore_NoBindingIsNeeded()
        => Analyze(bus =>
        {
            bus.AddHandler<StoreBoundHandler>();
            bus.AddMessageStore(A, _ => { });
        }).ShouldBeEmpty();

    /// <summary>
    /// The guarantee the deleted <c>AddOutboxInboxProcessing</c> test pinned, on its successor: the
    /// engine is registered once however many stores and calls register it.
    /// </summary>
    [Fact]
    public void TheEngine_IsRegisteredOnce_WhateverTheNumberOfStoresAndCalls()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging(bus => bus.AddMessageStore(A, _ => { }).AddMessageStore(A, _ => { }));
        services.AddMicroKitMessaging(bus => bus.AddMessageStore(B, _ => { }));

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType!.Name)
            .ShouldBe(["MessagingCompositionValidator", "MessageStoreEngine"], ignoreOrder: true);
        services.Count(d => d.ServiceType == typeof(IOutboxProcessor) && Equals(d.ServiceKey, A)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IInboxProcessor) && Equals(d.ServiceKey, B)).ShouldBe(1);
    }

    private static IReadOnlyList<string> Analyze(Action<MessagingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddMicroKitMessaging(configure);
        var composition = services.Select(d => d.ImplementationInstance).OfType<MessagingComposition>().Single();
        return MessagingCompositionAnalysis.Analyze(composition);
    }

    [MessageContract("tests.stores.bound")]
    private sealed record BoundEvent : IIntegrationEvent;

    private sealed class StoreBoundHandler : IMessageHandler<BoundEvent>
    {
        public ValueTask HandleAsync(BoundEvent message, MessageContext context, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
