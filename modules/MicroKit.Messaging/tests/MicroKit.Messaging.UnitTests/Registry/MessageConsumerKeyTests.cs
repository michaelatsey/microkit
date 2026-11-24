using System.Reflection;
using System.Reflection.Emit;

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

    /// <summary>
    /// V10 (#210 D7): the inbox claim joins a consumer key and a tenant id with U+001F, so a key that
    /// contains it could match another pair: <see cref="MessageConsumerKey.Of"/> refuses it, and a
    /// handler whose key does fails at registration. No C# identifier contains it: the type is emitted.
    /// </summary>
    [Fact]
    public void AHandlerWhoseConsumerKeyContainsAUnitSeparator_FailsAtRegistration()
    {
        var handlerType = typeof(SeparatorHandler<>).MakeGenericType(SeparatorNamedType());
        handlerType.AssemblyQualifiedName!.ShouldContain(UnitSeparator, Case.Sensitive, "control: its key would contain it");

        Should.Throw<ArgumentException>(() => MessageConsumerKey.Of(handlerType)).ParamName.ShouldBe("handlerType");

        var services = new ServiceCollection();
        var register = typeof(MessagingBuilder).GetMethod(nameof(MessagingBuilder.AddHandler))!.MakeGenericMethod(handlerType);
        var thrown = Should.Throw<TargetInvocationException>(() => services.AddMicroKitMessaging(bus => register.Invoke(bus, null)));
        var refused = thrown.InnerException.ShouldBeOfType<ArgumentException>();
        refused.Message.ShouldContain("Bad<U+001F>Name", Case.Sensitive, "the invisible character is spelled out");
        services.ShouldNotContain(d => d.ServiceType == handlerType, "a refused handler leaves nothing registered");
    }

    [Fact]
    public void AGenericHandlerWithNoUnitSeparatorInItsKey_IsKeyedAsBefore()
    {
        var key = MessageConsumerKey.Of(typeof(SeparatorHandler<MessageConsumerKeyTests>));

        var assembly = typeof(MessageConsumerKeyTests).Assembly.GetName().Name;
        key.ShouldBe($"{typeof(SeparatorHandler<>).FullName}[[{typeof(MessageConsumerKeyTests).FullName}, {assembly}]], {assembly}");
    }

    /// <summary>V10 (#210 A2): a local consumer's name is an inbox key too, whatever its source.</summary>
    [Fact]
    public void ALocalConsumerNamedWithAUnitSeparator_IsRefused()
    {
        static ValueTask Invoke(IServiceProvider services, object message, MessageContext context, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        Should.Throw<ArgumentException>(
                () => new LocalConsumer(typeof(object), $"tests.local{UnitSeparator}consumer", typeof(object), Invoke))
            .ParamName.ShouldBe("ConsumerType");
        new LocalConsumer(typeof(object), "tests.local.consumer", typeof(object), Invoke)
            .ConsumerType.ShouldBe("tests.local.consumer", "control: a name without it is accepted");
    }

    private const string UnitSeparator = "\u001F";

    /// <summary>A public type whose name contains U+001F, which no C# source can declare.</summary>
    private static Type SeparatorNamedType()
        => AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MicroKit.Messaging.Tests.Emitted"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Emitted")
            .DefineType($"Emitted.Bad{UnitSeparator}Name", TypeAttributes.Public | TypeAttributes.Sealed)
            .CreateType();
}

/// <summary>A generic handler: its consumer key carries its type argument's name.</summary>
internal sealed class SeparatorHandler<T> : IMessageHandler<MicroKit.Messaging.UnitTests.Bus.OrderPlaced>
{
    public ValueTask HandleAsync(MicroKit.Messaging.UnitTests.Bus.OrderPlaced message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
