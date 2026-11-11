namespace MicroKit.Messaging.UnitTests.DI;

using MicroKit.Messaging.UnitTests.Bus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// The verification of #187, host-based: one entry point, options bound from configuration and
/// validated at start, the composition validated at start, the service name's precedence, and the
/// in-memory broker routing by endpoint. The quick start itself is run by
/// <c>ReadmeSnippetTests</c>, from the README's own code.
/// </summary>
public sealed class SingleEntryPointTests
{
    private const string Section = MessagingOptions.SectionName;

    // ── Fails at host start ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnInvalidOptionValue_FailsAtHostStart_NamingTheKeyAndTheRange()
    {
        using var host = BuildHost(
            bus => bus.ServiceName = "svc",
            configuration: new() { [$"{Section}:Outbox:BatchSize"] = "0" });

        var ex = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"{Section}:Outbox:BatchSize must be at least 1; it is 0.");
    }

    [Fact]
    public async Task OutOfOrderPollingIntervals_FailAtHostStart()
    {
        using var host = BuildHost(bus => bus
            .Configure(o => o.Inbox.PollingInterval = TimeSpan.FromMinutes(5))
            .ServiceName = "svc");

        var ex = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"{Section}:Inbox:PollingInterval (00:05:00) must not exceed {Section}:Inbox:MaxPollingInterval (00:01:00)");
    }

    [Fact]
    public async Task ADelayBeyondWhatTaskDelayAccepts_FailsAtHostStart()
    {
        using var host = BuildHost(bus => bus
            .Configure(o => o.Outbox.RetentionInterval = MessagingOptionsValidator.MaxDelay + TimeSpan.FromMilliseconds(1))
            .ServiceName = "svc");

        var ex = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"{Section}:Outbox:RetentionInterval must be greater than zero and at most");
    }

    [Theory]
    [InlineData("Outbox:LockDuration")]
    [InlineData("Inbox:LeaseDuration")]
    public async Task ALeaseThatOverflowsTheClock_FailsAtHostStart(string key)
    {
        using var host = BuildHost(
            bus => bus.ServiceName = "svc",
            configuration: new() { [$"{Section}:{key}"] = TimeSpan.MaxValue.ToString() });

        var ex = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"{Section}:{key} must be greater than zero and at most");
        ex.Message.ShouldContain("DateTimeOffset.MaxValue");
    }

    [Fact]
    public async Task ARetentionThatOverflowsTheClock_FailsAtHostStart()
    {
        using var host = BuildHost(bus => bus
            .Configure(o => o.Inbox.RetentionDays = int.MaxValue)
            .ServiceName = "svc");

        var ex = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"{Section}:Inbox:RetentionDays must be at most");
    }

    [Fact]
    public void ANegativeRetention_StillDisablesRetention_AndPassesValidation()
    {
        using var host = BuildHost(
            bus => bus.ServiceName = "svc",
            configuration: new() { [$"{Section}:Outbox:RetentionDays"] = "-1" });

        host.Services.GetRequiredService<OutboxProcessorOptions>().RetentionDays.ShouldBe(-1);
    }

    [Fact]
    public async Task AMissingMessageContract_FailsAtHostStart()
    {
        using var host = BuildHost(bus =>
        {
            bus.ServiceName = "svc";
            bus.AddHandler<UncontractedHandler>();
            bus.UseInMemoryTransport();
        });

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"'{typeof(Uncontracted).FullName}' is handled by '{typeof(UncontractedHandler).FullName}' but has no [MessageContract]");
    }

    [Fact]
    public async Task ADuplicatedContractName_FailsAtHostStart()
    {
        using var host = BuildHost(bus =>
        {
            bus.ServiceName = "svc";
            bus.Publishes<OrderPlaced>().Publishes<OrderPlacedImpostor>();
            bus.UseInMemoryTransport();
        });

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("Wire type 'tests.bus.order-placed.v1' is claimed by");
    }

    [Fact]
    public async Task APublicationWithNoTransport_FailsAtHostStart()
    {
        using var host = BuildHost(bus =>
        {
            bus.ServiceName = "svc";
            bus.Publishes<OrderPlaced>();
        });

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("'OrderPlaced' is declared with bus.Publishes<T>(), but no IMessageTransport is registered");
        ex.Message.ShouldContain("bus.UseInMemoryTransport()");
    }

    [Theory]
    [InlineData(typeof(IOutboxProcessorStore), "An IOutboxProcessorStore is registered, but the outbox engine is not")]
    [InlineData(typeof(IInboxProcessorStore), "An IInboxProcessorStore is registered, but the inbox engine is not")]
    public async Task AStoreWithoutItsWorker_FailsAtHostStart(Type store, string expected)
    {
        using var host = BuildHost(
            bus => bus.ServiceName = "svc",
            services => services.AddScoped(store, _ => Substitute.For([store], [])));

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain(expected);
        ex.Message.ShouldContain("AddOutboxInboxProcessing()");
    }

    [Fact]
    public async Task EveryCompositionProblem_IsListedInOneExceptionAtHostStart()
    {
        using var host = BuildHost(
            bus =>
            {
                bus.ServiceName = "svc";
                bus.Publishes<Uncontracted>();
            },
            services => services.AddScoped(_ => Substitute.For<IInboxProcessorStore>()));

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("(3 problems)");
        ex.Message.ShouldContain("has no [MessageContract]");
        ex.Message.ShouldContain("no IMessageTransport is registered");
        ex.Message.ShouldContain("An IInboxProcessorStore is registered");
    }

    /// <summary>
    /// The validator runs before the in-memory transport binds its endpoint: an invalid
    /// composition binds nothing. The problem chosen is one the registry cannot see — a store
    /// without its engine — so only the validator's position can keep the endpoint unbound.
    /// </summary>
    [Fact]
    public async Task AnInvalidComposition_BindsNoEndpoint()
    {
        var broker = new InMemoryBroker();
        await using var invalid = BusTestHost.Build(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            broker,
            services => services.AddScoped(_ => Substitute.For<IOutboxProcessorStore>()),
            "svc.invalid",
            out _);

        await Should.ThrowAsync<IntegrationEventConfigurationException>(() => invalid.StartHostAsync());

        broker.EndpointsBoundTo("tests.bus.order-placed.v1").ShouldBeEmpty();

        // CONTROL: the same composition without the stray store binds its endpoint.
        await using var valid = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker, serviceName: "svc.valid");
        broker.EndpointsBoundTo("tests.bus.order-placed.v1").ShouldBe(["svc.valid"]);
    }

    /// <summary>
    /// The binding does not rely on the start order: under concurrent start the in-memory transport
    /// validates the composition itself before it binds.
    /// </summary>
    [Fact]
    public async Task AnInvalidComposition_UnderConcurrentStart_StillBindsNoEndpoint()
    {
        var broker = new InMemoryBroker();
        await using var invalid = BusTestHost.Build(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            broker,
            services => services
                .Configure<HostOptions>(o => o.ServicesStartConcurrently = true)
                .AddScoped(_ => Substitute.For<IOutboxProcessorStore>()),
            "svc.concurrent",
            out _);

        await Should.ThrowAsync<Exception>(() => invalid.StartHostAsync());

        broker.EndpointsBoundTo("tests.bus.order-placed.v1").ShouldBeEmpty();
    }

    [Fact]
    public async Task AnOutboxPublisherWithNoTransportDispatcher_FailsAtHostStart()
    {
        using var host = BuildHost(
            bus =>
            {
                bus.ServiceName = "svc";
                bus.Services.AddSingleton(Substitute.For<IMessageTransport>());
            },
            services => services.AddScoped(_ => Substitute.For<IIntegrationEventPublisher>()));

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("IIntegrationEventPublisher stages publications in the outbox, but no transport dispatcher is registered");
    }

    [Fact]
    public async Task AnOutboxPublisherWithTheInMemoryTransport_PassesTheDispatcherCheck()
    {
        using var host = BuildHost(
            bus =>
            {
                bus.ServiceName = "svc";
                bus.UseInMemoryTransport();
            },
            services => services.AddScoped(_ => Substitute.For<IIntegrationEventPublisher>()));

        await host.StartAsync();
        await host.StopAsync();
    }

    // ── Options from configuration ───────────────────────────────────────────────────────────

    [Fact]
    public void OptionsBoundFromAnInMemoryConfiguration_AreApplied()
    {
        using var host = BuildHost(
            _ => { },
            configuration: new()
            {
                [$"{Section}:ServiceName"] = "shop.from-configuration",
                [$"{Section}:Outbox:BatchSize"] = "42",
                [$"{Section}:Outbox:LockDuration"] = "00:07:00",
                [$"{Section}:Inbox:MaxRetries"] = "9",
                [$"{Section}:Inbox:RetentionDays"] = "45",
            });

        var options = host.Services.GetRequiredService<IOptions<MessagingOptions>>().Value;
        options.ServiceName.ShouldBe("shop.from-configuration");
        options.Outbox.BatchSize.ShouldBe(42);
        options.Outbox.LockDuration.ShouldBe(TimeSpan.FromMinutes(7));
        options.Inbox.MaxRetries.ShouldBe(9);
        options.Inbox.RetentionDays.ShouldBe(45);

        // The processors' own option types are forwards of the same values.
        host.Services.GetRequiredService<OutboxProcessorOptions>().BatchSize.ShouldBe(42);
        host.Services.GetRequiredService<InboxProcessorOptions>().MaxRetries.ShouldBe(9);
    }

    [Fact]
    public void BusConfigure_AppliesAfterTheConfigurationBinding()
    {
        using var host = BuildHost(
            bus => bus.Configure(o => o.Outbox.BatchSize = 7),
            configuration: new() { [$"{Section}:ServiceName"] = "svc", [$"{Section}:Outbox:BatchSize"] = "42" });

        host.Services.GetRequiredService<OutboxProcessorOptions>().BatchSize.ShouldBe(7);
    }

    /// <summary>A container composed by hand has no IConfiguration; the options keep their defaults.</summary>
    [Fact]
    public void ABareContainer_WithoutConfiguration_KeepsTheDefaults()
    {
        var services = new ServiceCollection();
        services.AddMicroKitMessaging(bus => bus.ServiceName = "svc");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.Outbox.BatchSize
            .ShouldBe(new OutboxProcessorOptions().BatchSize);
    }

    // ── ServiceName: ApplicationName < configuration < bus.Configure < bus.ServiceName ──────

    [Fact]
    public void ServiceName_WithNothingElseSet_IsTheApplicationName()
        => ServiceNameOf(BuildHost(_ => { }, applicationName: "shop.app")).ShouldBe("shop.app");

    [Fact]
    public void ServiceName_FromConfiguration_OverridesTheApplicationName()
        => ServiceNameOf(BuildHost(
            _ => { },
            configuration: new() { [$"{Section}:ServiceName"] = "shop.config" },
            applicationName: "shop.app")).ShouldBe("shop.config");

    [Fact]
    public void ServiceName_FromBusConfigure_OverridesConfiguration()
        => ServiceNameOf(BuildHost(
            bus => bus.Configure(o => o.ServiceName = "shop.configure"),
            configuration: new() { [$"{Section}:ServiceName"] = "shop.config" },
            applicationName: "shop.app")).ShouldBe("shop.configure");

    [Fact]
    public void ServiceName_FromBusServiceName_OverridesBusConfigure()
        => ServiceNameOf(BuildHost(
            bus =>
            {
                bus.ServiceName = "shop.code";
                bus.Configure(o => o.ServiceName = "shop.configure");
            },
            configuration: new() { [$"{Section}:ServiceName"] = "shop.config" },
            applicationName: "shop.app")).ShouldBe("shop.code");

    /// <summary>bus.ServiceName wins even over a bus.Configure registered by a later call.</summary>
    [Fact]
    public void ServiceName_FromBusServiceName_OverridesALaterCallsBusConfigure()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMicroKitMessaging(bus => bus.ServiceName = "shop.code");
        builder.Services.AddMicroKitMessaging(bus => bus.Configure(o => o.ServiceName = "shop.configure"));

        ServiceNameOf(builder.Build()).ShouldBe("shop.code");
    }

    [Fact]
    public void ServiceName_AnEmptyConfigurationValue_CountsAsAbsent()
        => ServiceNameOf(BuildHost(
            _ => { },
            configuration: new() { [$"{Section}:ServiceName"] = "" },
            applicationName: "shop.app")).ShouldBe("shop.app");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ServiceName_SetBlankOnTheBuilder_ThrowsAtTheCall(string blank)
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddMicroKitMessaging(bus => bus.ServiceName = blank));
    }

    [Fact]
    public void ServiceName_SetNullOnTheBuilder_LeavesItUnset()
    {
        var services = new ServiceCollection();
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.code";
            bus.ServiceName = null;
        });
        using var provider = services.BuildServiceProvider();

        // No configuration and no host environment either: nothing names the service.
        var ex = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<MessagingOptions>>().Value);
        ex.Message.ShouldContain($"set bus.ServiceName or {Section}:ServiceName");
    }

    [Fact]
    public async Task TwoCallsSettingDifferentServiceNames_FailAtHostStart()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMicroKitMessaging(bus => bus.ServiceName = "shop.orders");
        builder.Services.AddMicroKitMessaging(bus => bus.ServiceName = "shop.billing");
        using var host = builder.Build();

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("bus.ServiceName is set to 'shop.orders' and 'shop.billing'");
    }

    // ── A handler of two types, on the bus ───────────────────────────────────────────────────

    public static TheoryData<string> TwoTypeHandlers => [nameof(PlacedThenShippedHandler), nameof(ShippedThenPlacedHandler)];

    /// <summary>
    /// One handler implementing <see cref="IMessageHandler{T}"/> for two types reaches each of its
    /// methods, whichever order it declares them in, and registering it twice changes nothing: one
    /// invocation per message. The inbox half is in <c>MultiTypeHandlerInboxTests</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(TwoTypeHandlers))]
    public async Task AHandlerOfTwoTypes_OnTheBus_ReachesEachMethodOnce_AndNothingIsDeadLettered(string handler)
    {
        await using var host = await BusTestHost.StartAsync(bus =>
        {
            if (handler == nameof(PlacedThenShippedHandler))
            {
                bus.AddHandler<PlacedThenShippedHandler>().AddHandler<PlacedThenShippedHandler>();
            }
            else
            {
                bus.AddHandler<ShippedThenPlacedHandler>().AddHandler<ShippedThenPlacedHandler>();
            }

            bus.AddHandler<InvoiceIssuedHandler>();
        });

        var placed = new OrderPlaced(Guid.NewGuid());
        var shipped = new OrderShipped(Guid.NewGuid());
        await host.PublishAsync(placed);
        await host.PublishAsync(shipped);

        // The consume loop delivers one message at a time, so once the sentinel has run, every
        // invocation of the two messages before it has too.
        await host.PublishAsync(new InvoiceIssued(new Serialization.Money(1)));
        await Eventually.HoldsAsync(
            () => host.Recorder.Deliveries.Any(d => d.Handler == nameof(InvoiceIssuedHandler)), "the sentinel ran");

        host.Recorder.Deliveries.Where(d => d.Handler != nameof(InvoiceIssuedHandler))
            .Select(d => (d.Handler, d.Message))
            .ShouldBe([($"{handler}:placed", (object)placed), ($"{handler}:shipped", shipped)]);
        host.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public void AddHandler_OfATypeHandlingNothing_ThrowsAtTheCall()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() => services.AddMicroKitMessaging(bus => bus.AddHandler<NotAHandler>()));
        ex.Message.ShouldContain("implements no IMessageHandler<T>");
    }

    /// <summary>
    /// A partial type load neither fails the scan nor the start, and is not silent: the types that
    /// loaded are registered, and the start logs ONE Warning naming the assembly, the type that
    /// failed and the first loader error. Driven through the scan's internal type source — an
    /// assembly whose <c>GetTypes()</c> genuinely fails cannot be produced cheaply here — with a
    /// real <see cref="TypeLoadException"/>.
    /// </summary>
    [Fact]
    public async Task HandlerDiscovery_WhenSomeTypesCannotLoad_RegistersTheRest_AndWarnsOnceAtStart()
    {
        var loadFailure = Should.Throw<TypeLoadException>(
            () => Type.GetType("Missing.Handler, MicroKit.Messaging.UnitTests", throwOnError: true));
        var logs = new Readme.CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<BusRecorder>().AddScoped<ScopeMarker>();
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "svc.partial";
#pragma warning disable IL2026 // the scan is the subject under test; this assembly is not trimmed
            bus.AddHandlersFrom("Partial.Assembly", () => throw new System.Reflection.ReflectionTypeLoadException(
                [typeof(FirstOrderPlacedHandler), null], [loadFailure]));
#pragma warning restore IL2026
            bus.UseInMemoryTransport();
        });
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetRequiredService<MessageHandlerRegistry>().GetHandlers(typeof(OrderPlaced))
            .ShouldHaveSingleItem().HandlerType.ShouldBe(typeof(FirstOrderPlacedHandler));
        var warning = logs.Entries.Where(e => e.Id.Id == 3016).ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("'Partial.Assembly'");
        warning.Message.ShouldContain("Missing.Handler");
        warning.Message.ShouldContain(loadFailure.Message);
        await host.StopAsync();
    }

    [Fact]
    public void ServiceName_SetOnTheBuilderAfterTheCallReturned_StillCounts()
    {
        var services = new ServiceCollection();
        var bus = services.AddMicroKitMessaging(_ => { });
        bus.ServiceName = "shop.late";
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.ServiceName.ShouldBe("shop.late");
    }

    [Fact]
    public void AddHandlersFromAssemblyContaining_RegistersEveryHandlerOfTheAssembly_LikeAddHandler()
    {
        var services = new ServiceCollection();
#pragma warning disable IL2026 // the scan is the subject under test; this assembly is not trimmed
        services.AddMicroKitMessaging(bus => bus.AddHandlersFromAssemblyContaining<SingleEntryPointTests>());
#pragma warning restore IL2026
        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetRequiredService<MessageHandlerRegistry>();

        handlers.GetHandlers(typeof(OrderPlaced)).Select(h => h.HandlerType)
            .ShouldContain(typeof(FirstOrderPlacedHandler));
        handlers.TryGetInvoker(typeof(PlacedThenShippedHandler).AssemblyQualifiedName!, typeof(OrderShipped), out _)
            .ShouldBeTrue("a scanned handler of two types holds one entry per type");
        handlers.GetHandlers(typeof(OrderPlaced)).Select(h => h.HandlerType)
            .ShouldNotContain(typeof(AbstractOrderPlacedHandler), "abstract classes are skipped");
        services.ShouldContain(d => d.ServiceType == typeof(FirstOrderPlacedHandler) && d.Lifetime == ServiceLifetime.Transient);
    }

    // ── D9: the in-memory broker routes by endpoint, through the public send path ───────────

    [Fact]
    public async Task TwoHostsWithDifferentServiceNames_OnOneBroker_EachReceiveTheirCopy()
    {
        var broker = new InMemoryBroker();
        await using var b = await BusTestHost.StartAsync(bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker, serviceName: "svc.b");
        await using var c = await BusTestHost.StartAsync(bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker, serviceName: "svc.c");

        var id = await b.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        await Eventually.HoldsAsync(
            () => b.Recorder.Deliveries.Count == 1 && c.Recorder.Deliveries.Count == 1, "each endpoint has its copy");
        b.Recorder.Deliveries.Single().Context.MessageId.ShouldBe(id);
        c.Recorder.Deliveries.Single().Context.MessageId.ShouldBe(id);
        b.DeadLetters.ShouldBeEmpty();
        c.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task TwoHostsWithTheSameServiceName_OnOneBroker_ReceiveEachMessageOnceBetweenThem()
    {
        const int Count = 20;
        var broker = new InMemoryBroker();
        await using var first = await BusTestHost.StartAsync(bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker, serviceName: "svc.shared");
        await using var second = await BusTestHost.StartAsync(bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker, serviceName: "svc.shared");

        var ids = new List<MessageId>();
        for (var i = 0; i < Count; i++)
        {
            ids.Add(await first.PublishAsync(new OrderPlaced(Guid.NewGuid())));
        }

        await Eventually.HoldsAsync(
            () => first.Recorder.Deliveries.Count + second.Recorder.Deliveries.Count >= Count, "every message handled");
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        first.Recorder.Deliveries.Concat(second.Recorder.Deliveries)
            .Select(d => d.Context.MessageId)
            .ShouldBe(ids, ignoreOrder: true, "each message once between the two competing hosts");
        broker.GetDeadLetters("svc.shared").ShouldBeEmpty();
    }

    /// <summary>
    /// Endpoints B and C both bound to one contract: B fails until it dead-letters while C handles
    /// the message once. Attempts and dead letters are kept per endpoint, so the same message id is
    /// tracked separately on each.
    /// </summary>
    [Fact]
    public async Task AFailingEndpoint_DeadLettersItsCopy_WhileAnotherEndpointHandlesItsOwnOnce()
    {
        var broker = new InMemoryBroker { MaxDeliveryAttempts = 3 };
        await using var b = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FlakyOrderPlacedHandler>(),
            broker,
            services => services.AddSingleton(new FlakyScript { FailuresBeforeSuccess = int.MaxValue }),
            serviceName: "svc.b");
        await using var c = await BusTestHost.StartAsync(bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker, serviceName: "svc.c");

        var id = await c.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        await Eventually.HoldsAsync(() => b.DeadLetters.Count == 1, "B dead-letters its copy at the bound");
        await Eventually.HoldsAsync(() => c.Recorder.Deliveries.Count == 1, "C handles its copy");

        b.DeadLetters.Single().Id.ShouldBe(id.ToString());
        b.FailedAttempts(id.ToString()).ShouldBe(3);
        c.FailedAttempts(id.ToString()).ShouldBe(0, "the same message id, tracked separately on C");
        c.DeadLetters.ShouldBeEmpty();
        c.Recorder.Deliveries.Single().Context.MessageId.ShouldBe(id);
    }

    [Fact]
    public async Task AMessageNoEndpointIsBoundTo_ReachesNoOne_AndTheTransportLogsAWarning()
    {
        var logs = new Readme.CapturingLoggerProvider();
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            services: services => services.AddLogging(l => l.AddProvider(logs)));

        var id = await host.PublishAsync(new OrderShipped(Guid.NewGuid()));

        logs.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Id.Id == 3014 && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
        host.Recorder.Deliveries.ShouldBeEmpty();
        host.DeadLetters.ShouldBeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static IHost BuildHost(
        Action<MessagingBuilder> compose,
        Action<IServiceCollection>? services = null,
        Dictionary<string, string?>? configuration = null,
        string applicationName = "MicroKit.Messaging.UnitTests")
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = applicationName,
        });

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        services?.Invoke(builder.Services);
        builder.Services.AddMicroKitMessaging(compose);
        return builder.Build();
    }

    private static string? ServiceNameOf(IHost host)
    {
        using (host)
        {
            return host.Services.GetRequiredService<IOptions<MessagingOptions>>().Value.ServiceName;
        }
    }
}

internal sealed record Uncontracted : IIntegrationEvent;

internal sealed class UncontractedHandler : IMessageHandler<Uncontracted>
{
    public ValueTask HandleAsync(Uncontracted message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

/// <summary>Claims the wire type of <see cref="OrderPlaced"/>.</summary>
[MessageContract("tests.bus.order-placed")]
internal sealed record OrderPlacedImpostor(Guid OrderId) : IIntegrationEvent;

internal sealed class NotAHandler;

internal abstract class AbstractOrderPlacedHandler : IMessageHandler<OrderPlaced>
{
    public abstract ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default);
}

internal sealed class PlacedThenShippedHandler(BusRecorder recorder, ScopeMarker scope)
    : IMessageHandler<OrderPlaced>, IMessageHandler<OrderShipped>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered($"{nameof(PlacedThenShippedHandler)}:placed", message, context, scope.Id);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderShipped message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered($"{nameof(PlacedThenShippedHandler)}:shipped", message, context, scope.Id);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ShippedThenPlacedHandler(BusRecorder recorder, ScopeMarker scope)
    : IMessageHandler<OrderShipped>, IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderShipped message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered($"{nameof(ShippedThenPlacedHandler)}:shipped", message, context, scope.Id);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered($"{nameof(ShippedThenPlacedHandler)}:placed", message, context, scope.Id);
        return ValueTask.CompletedTask;
    }
}
