namespace MicroKit.Messaging.UnitTests.Registry;

/// <summary>
/// The inbox key of a consumer is version-free (#188): it survives an assembly-version bump, so a
/// redelivery across a deployment still finds its row and runs no handler twice.
/// </summary>
public sealed class MessageConsumerKeyTests
{
    [Fact]
    public void TheKey_IsIdenticalForTwoAssemblyVersionsOfTheSameType()
    {
        var v1 = MessageConsumerKey.FromAssemblyQualifiedName(
            "Shop.Orders.OrderPlacedHandler, Shop.Orders, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        var v2 = MessageConsumerKey.FromAssemblyQualifiedName(
            "Shop.Orders.OrderPlacedHandler, Shop.Orders, Version=2.3.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef");

        v1.ShouldBe(v2);
        v1.ShouldBe("Shop.Orders.OrderPlacedHandler, Shop.Orders");
    }

    [Fact]
    public void TheKeyOfAType_IsItsFullNameAndAssemblyName_WithNoVersionParts()
    {
        var key = MessageConsumerKey.Of(typeof(MessageConsumerKeyTests));

        key.ShouldBe($"{typeof(MessageConsumerKeyTests).FullName}, {typeof(MessageConsumerKeyTests).Assembly.GetName().Name}");
        key.ShouldNotContain("Version=");
        key.ShouldNotContain("Culture=");
        key.ShouldNotContain("PublicKeyToken=");
    }

    [Fact]
    public void AGenericHandlersTypeArguments_AreStrippedTheSameWay()
    {
        var key = MessageConsumerKey.Of(typeof(List<MessageConsumerKeyTests>));

        key.ShouldNotContain("Version=");
        key.ShouldContain($"[[{typeof(MessageConsumerKeyTests).FullName}, {typeof(MessageConsumerKeyTests).Assembly.GetName().Name}]]");
    }

    [Fact]
    public void BusHandlers_AreKeyedByIt()
    {
        var services = new ServiceCollection();
        services.AddMicroKitMessaging(bus => bus.AddHandler<MicroKit.Messaging.UnitTests.Bus.FirstOrderPlacedHandler>());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<MessageHandlerRegistry>();
        registry.TryGetInvoker(MessageConsumerKey.Of(typeof(MicroKit.Messaging.UnitTests.Bus.FirstOrderPlacedHandler)), typeof(MicroKit.Messaging.UnitTests.Bus.OrderPlaced), out _)
            .ShouldBeTrue("a bus handler's inbox key is its version-free key");
    }
}
