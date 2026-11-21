using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.UnitTests.Stores;

/// <summary>
/// D3 (#188), rule by rule: a publication's store is <see cref="PublishOptions.Store"/>, the
/// delivery's store, the only store, or the store bound to the type's assembly — first match wins,
/// and no match is an error, never a guess.
/// </summary>
public sealed class StoreResolutionTests
{
    private static readonly MessageStoreKey A = new("tests:a");
    private static readonly MessageStoreKey B = new("tests:b");

    [Fact]
    public void WithNoStore_APublicationGoesStraightToTheTransport()
        => Resolver().ForPublication(typeof(ResolvedEvent), null, null).ShouldBeNull();

    [Fact]
    public void WithNoStore_AnExplicitStoreIsRefused()
        => Should.Throw<IntegrationEventPublishException>(() => Resolver().ForPublication(typeof(ResolvedEvent), A, null))
            .Message.ShouldContain("no store is configured");

    [Fact]
    public void WithOneStore_ItIsTheStore_WithNoBinding()
        => Resolver(A).ForPublication(typeof(ResolvedEvent), null, null).ShouldBe(A);

    [Fact]
    public void TheExplicitStore_WinsOverTheBinding()
        => Resolver(A, B, bindTo: A).ForPublication(typeof(ResolvedEvent), B, null).ShouldBe(B);

    [Fact]
    public void TheExplicitStore_MustBeConfigured()
        => Should.Throw<IntegrationEventPublishException>(
                () => Resolver(A, B).ForPublication(typeof(ResolvedEvent), new MessageStoreKey("tests:none"), null))
            .Message.ShouldContain("'tests:none', which is not configured");

    [Fact]
    public void InsideADelivery_TheExplicitStore_MayNameOnlyTheDeliverysStore()
    {
        var sut = Resolver(A, B);

        sut.ForPublication(typeof(ResolvedEvent), B, deliveryStore: B).ShouldBe(B);
        Should.Throw<IntegrationEventPublishException>(() => sut.ForPublication(typeof(ResolvedEvent), A, deliveryStore: B))
            .Message.ShouldContain("from a delivery of store 'tests:b'");
    }

    [Fact]
    public void InsideADelivery_TheDeliverysStore_WinsOverTheBinding()
        => Resolver(A, B, bindTo: A).ForPublication(typeof(ResolvedEvent), null, deliveryStore: B).ShouldBe(B);

    [Fact]
    public void WithSeveralStores_TheBindingOfTheTypesAssembly_Decides()
        => Resolver(A, B, bindTo: B).ForPublication(typeof(ResolvedEvent), null, null).ShouldBe(B);

    [Fact]
    public void WithSeveralStores_AndNoRuleMatching_ThePublicationFails_NamingTheStoresAndTheFix()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Resolver(A, B).ForPublication(typeof(ResolvedEvent), null, null));

        ex.Message.ShouldContain("2 stores are configured ('tests:a', 'tests:b')");
        ex.Message.ShouldContain("ForAssemblyContaining<T>()");
        ex.Message.ShouldContain("PublishOptions.Store");
    }

    [Fact]
    public void AHandlersStore_IsTheOnlyStore_OrItsAssemblysBinding()
    {
        Resolver().ForHandler(typeof(StoreResolutionTests)).ShouldBeNull();
        Resolver(A).ForHandler(typeof(StoreResolutionTests)).ShouldBe(A);
        Resolver(A, B, bindTo: B).ForHandler(typeof(StoreResolutionTests)).ShouldBe(B);
        Should.Throw<IntegrationEventConfigurationException>(() => Resolver(A, B).ForHandler(typeof(StoreResolutionTests)))
            .Message.ShouldContain("is bound to no store");
    }

    private static MessageStoreResolver Resolver(params MessageStoreKey[] stores) => Resolver(stores, null);

    private static MessageStoreResolver Resolver(MessageStoreKey first, MessageStoreKey second, MessageStoreKey bindTo)
        => Resolver([first, second], bindTo);

    private static MessageStoreResolver Resolver(MessageStoreKey[] stores, MessageStoreKey? bindTo)
    {
        var composition = new MessagingComposition();
        foreach (var store in stores)
        {
            var registration = composition.GetOrAddStore(store, out _);
            if (store == bindTo)
            {
                registration.Assemblies.Add(typeof(StoreResolutionTests).Assembly);
            }
        }

        return new MessageStoreResolver(composition, new MicroKit.Messaging.Stores.StaticMessageStoreSet(composition));
    }

    [MessageContract("tests.stores.resolved")]
    private sealed record ResolvedEvent : IIntegrationEvent;
}
