namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// Which notification handler registrations become local consumers (#188 D8), and C3: a
/// registration that cannot be turned into a concrete, closed handler is reported, never skipped.
/// </summary>
public sealed class NotificationHandlerSourceTests
{
    private static NotificationHandlerSource Source(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return new NotificationHandlerSource(services);
    }

    [Fact]
    public void EachClosedHandlerOfADomainEventNotification_IsAConsumerOfItsOwn_NamedByItsType()
    {
        var source = Source(s => s
            .AddTransient<INotificationHandler<OrderPlacedNotification>, FirstOrderHandler>()
            .AddTransient<INotificationHandler<OrderPlacedNotification>, SecondOrderHandler>());

        var consumers = source.GetConsumers().ToList();

        consumers.Select(c => c.HandlerType).ShouldBe([typeof(FirstOrderHandler), typeof(SecondOrderHandler)]);
        consumers.ShouldAllBe(c => c.MessageType == typeof(OrderPlacedNotification));
        // The consumer type is the inbox key: the bus's own rule for a handler (#187).
        consumers[0].ConsumerType.ShouldBe(MessageConsumerKey.Of(typeof(FirstOrderHandler)));
        source.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void AHandlerOfAnotherNotification_IsLeftToMediatR()
    {
        var source = Source(s => s.AddTransient<INotificationHandler<PlainNotification>, PlainHandler>());

        source.GetConsumers().ShouldBeEmpty();
        source.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void AKeyedRegistration_IsNotMediatRs_AndIsIgnored()
    {
        var source = Source(s => s.AddKeyedTransient<INotificationHandler<OrderPlacedNotification>, FirstOrderHandler>("k"));

        source.GetConsumers().ShouldBeEmpty();
        source.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void AnOpenGenericHandler_IsReported_NamingIt()
    {
        var source = Source(s => s.AddTransient(typeof(INotificationHandler<>), typeof(OpenHandler<>)));

        source.GetConsumers().ShouldBeEmpty();
        source.Problems().ShouldHaveSingleItem().ShouldContain(typeof(OpenHandler<>).FullName!);
    }

    [Fact]
    public void AFactoryRegisteredHandler_IsReported_NamingItsNotification()
    {
        var source = Source(s => s.AddTransient<INotificationHandler<OrderPlacedNotification>>(
            sp => new FirstOrderHandler(new CallLog())));

        source.GetConsumers().ShouldBeEmpty();
        var problem = source.Problems().ShouldHaveSingleItem();
        problem.ShouldContain(typeof(OrderPlacedNotification).FullName!);
        problem.ShouldContain("a factory");
    }

    [Fact]
    public void ASingletonHandler_IsReported_NamingTheFix()
    {
        // A singleton would be shared by every delivery, and resolving it builds its siblings too.
        var source = Source(s => s.AddSingleton<INotificationHandler<OrderPlacedNotification>, FirstOrderHandler>());

        source.GetConsumers().ShouldBeEmpty();
        var problem = source.Problems().ShouldHaveSingleItem();
        problem.ShouldContain(typeof(FirstOrderHandler).FullName!);
        problem.ShouldContain("singleton");
        problem.ShouldContain("register it transient or scoped");
    }

    [Fact]
    public void AnInstanceRegisteredHandler_IsReported_NamingItsNotification()
    {
        var source = Source(s => s.AddSingleton<INotificationHandler<OrderPlacedNotification>>(
            new FirstOrderHandler(new CallLog())));

        source.GetConsumers().ShouldBeEmpty();
        var problem = source.Problems().ShouldHaveSingleItem();
        problem.ShouldContain(typeof(OrderPlacedNotification).FullName!);
        problem.ShouldContain("an instance");
    }

    [Fact]
    public void AHandlerOfAnAbstractDomainEventNotification_IsReported_NamingIt()
    {
        var source = Source(s => s.AddTransient<INotificationHandler<AbstractOrderNotification>, AbstractOrderHandler>());

        source.GetConsumers().ShouldBeEmpty();
        source.Problems().ShouldHaveSingleItem().ShouldContain(typeof(AbstractOrderHandler).FullName!);
    }

    [Fact]
    public async Task AConsumer_IsNotFailedByASiblingThatCannotBeBuilt_AndDisposesItsOwnHandler()
    {
        // Review finding (#188 P3): resolving every handler of the notification to pick one let a
        // sibling's missing dependency fail this consumer. Each consumer builds its own handler.
        var log = new CallLog();
        var services = new ServiceCollection()
            .AddSingleton(log)
            .AddTransient<INotificationHandler<OrderPlacedNotification>, FirstOrderHandler>()
            .AddTransient<INotificationHandler<OrderPlacedNotification>, UnbuildableOrderHandler>()
            .AddTransient<INotificationHandler<OrderPlacedNotification>, DisposableOrderHandler>();
        var consumers = new NotificationHandlerSource(services).GetConsumers().ToList();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        foreach (var consumer in consumers.Where(c => c.HandlerType != typeof(UnbuildableOrderHandler)))
        {
            await consumer.Invoke(scope.ServiceProvider, Notification(), Context(), CancellationToken.None);
        }

        log.Calls.ShouldBe([nameof(FirstOrderHandler), nameof(DisposableOrderHandler), "disposed"]);
    }

    [Fact]
    public void AHandlerOfAStructDomainEventsNotification_IsAConsumer()
    {
        // Covariance does not reach a value type: the scan must not skip it in silence (C3).
        var source = Source(s => s.AddTransient<INotificationHandler<StructPlacedNotification>, StructPlacedHandler>());

        source.GetConsumers().ShouldHaveSingleItem().MessageType.ShouldBe(typeof(StructPlacedNotification));
        source.Problems().ShouldBeEmpty();
    }

    private static OrderPlacedNotification Notification() => new(new OrderPlaced(Guid.NewGuid(), DateTimeOffset.UnixEpoch));

    private static MessageContext Context() => new()
    {
        MessageId = MessageId.New(),
        ContractName = string.Empty,
        ContractVersion = 0,
        Source = "tests",
        OccurredOnUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task AConsumer_RunsItsOwnHandler_FromTheScope_AndNoSibling()
    {
        var log = new CallLog();
        var services = new ServiceCollection()
            .AddSingleton(log)
            .AddTransient<INotificationHandler<OrderPlacedNotification>, FirstOrderHandler>()
            .AddTransient<INotificationHandler<OrderPlacedNotification>, SecondOrderHandler>();
        var second = new NotificationHandlerSource(services).GetConsumers()
            .Single(c => c.HandlerType == typeof(SecondOrderHandler));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await second.Invoke(
            scope.ServiceProvider,
            new OrderPlacedNotification(new OrderPlaced(Guid.NewGuid(), DateTimeOffset.UnixEpoch)),
            new MessageContext
            {
                MessageId = MessageId.New(),
                ContractName = string.Empty,
                ContractVersion = 0,
                Source = "tests",
                OccurredOnUtc = DateTimeOffset.UnixEpoch,
            },
            CancellationToken.None);

        log.Calls.ShouldBe([nameof(SecondOrderHandler)]);
    }
}
