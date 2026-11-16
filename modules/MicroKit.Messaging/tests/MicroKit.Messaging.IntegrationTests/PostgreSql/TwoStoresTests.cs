using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MicroKit.Messaging.IntegrationTests.SecondModule;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// V4 (#188): two <c>DbContext</c> types, each with its own outbox, in one host. Every publication
/// lands in the store D3 picks — <see cref="PublishOptions.Store"/>, the delivery's store, the store
/// bound to the message type's assembly — and the ambiguous and unbound setups fail at start or at
/// publication.
/// </summary>
/// <remarks>
/// The bindings are crossed on purpose: this assembly — the handlers — is bound to store B, and
/// <c>SecondModule</c> — the message type the handler publishes — to store A. Inside a delivery the
/// delivery's store (B) and the type's assembly (A) then disagree, so the assertion tells the two
/// rules apart; with aligned bindings a broken rule 2 would pass as rule 4.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class TwoStoresTests(PostgreSqlFixture fixture)
{
    private static readonly MessageStoreKey StoreA = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();
    private static readonly MessageStoreKey StoreB = EntityFrameworkOutbox.KeyOf<SecondMessagingDbContext>();

    [DockerRequiredFact]
    public async Task OutsideADelivery_APublicationLandsInTheStoreBoundToItsTypesAssembly()
    {
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString);

        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new SecondModuleEvent(Guid.NewGuid()));   // SecondModule → A
            await bus.PublishAsync(new StoreBEvent(Guid.NewGuid()));        // this assembly → B
            await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<SecondMessagingDbContext>().SaveChangesAsync();
        }

        (await host.OutboxOfAAsync()).ShouldHaveSingleItem().ContractName.ShouldBe("tests.second-module.event.v1");
        (await host.OutboxOfBAsync()).ShouldHaveSingleItem().ContractName.ShouldBe("tests.two-stores.b-event.v1");
    }

    [DockerRequiredFact]
    public async Task PublishOptionsStore_OverridesTheAssemblyBinding()
    {
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString);

        await using (var scope = host.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .PublishAsync(new SecondModuleEvent(Guid.NewGuid()), new PublishOptions { Store = StoreB });
            await scope.ServiceProvider.GetRequiredService<SecondMessagingDbContext>().SaveChangesAsync();
        }

        (await host.OutboxOfAAsync()).ShouldBeEmpty();
        (await host.OutboxOfBAsync()).ShouldHaveSingleItem().ContractName.ShouldBe("tests.second-module.event.v1");
    }

    [DockerRequiredFact]
    public async Task PublishOptionsStore_NamingNoConfiguredStore_IsRefused()
    {
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString);
        await using var scope = host.Provider.CreateAsyncScope();

        var ex = await Should.ThrowAsync<IntegrationEventPublishException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(
                new SecondModuleEvent(Guid.NewGuid()), new PublishOptions { Store = new MessageStoreKey("ef:Nowhere") }));

        ex.Message.ShouldContain("'ef:Nowhere', which is not configured");
        scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().ChangeTracker.Entries().ShouldBeEmpty();
        scope.ServiceProvider.GetRequiredService<SecondMessagingDbContext>().ChangeTracker.Entries().ShouldBeEmpty();
    }

    /// <summary>
    /// Inside a delivery of store B, the handler's publication lands in B although its type is
    /// bound to A: it commits with the handler's work, in the handler's store (D3 rule 2).
    /// </summary>
    [DockerRequiredFact]
    public async Task InsideADelivery_APublicationLandsInTheDeliverysStore()
    {
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString);
        await host.SeedInboxOfBAsync<PublishingStoreBHandler>(new StoreBEvent(Guid.NewGuid()));

        await host.DrainInboxOfBAsync();

        host.Recorder.Handled.ShouldHaveSingleItem();
        (await host.OutboxOfAAsync()).ShouldBeEmpty("the type's assembly binding loses to the delivery's store");
        (await host.OutboxOfBAsync()).ShouldHaveSingleItem().ContractName.ShouldBe("tests.second-module.event.v1");
    }

    /// <summary>Inside a delivery, an explicit store may only name the delivery's own.</summary>
    [DockerRequiredFact]
    public async Task InsideADelivery_PublishOptionsStoreNamingAnotherStore_IsRefusedAndNothingIsEnlisted()
    {
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString);
        await host.SeedInboxOfBAsync<CrossStorePublishingHandler>(new StoreBEvent(Guid.NewGuid()));

        await host.DrainInboxOfBAsync();

        var refusal = host.Recorder.Refusals.ShouldHaveSingleItem();
        refusal.ShouldBeOfType<IntegrationEventPublishException>()
            .Message.ShouldContain($"from a delivery of store '{StoreB}'");
        (await host.OutboxOfAAsync()).ShouldBeEmpty();
        (await host.OutboxOfBAsync()).ShouldBeEmpty();
    }

    [DockerRequiredFact]
    public async Task WithTwoStores_APublicationNoRuleResolves_FailsAtPublication()
    {
        // This assembly is bound to B; SecondModule to nothing — and nothing of it is handled here,
        // so the host is valid and only the publication is ambiguous.
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString, bindSecondModule: false);
        await using var scope = host.Provider.CreateAsyncScope();

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new SecondModuleEvent(Guid.NewGuid())));

        ex.Message.ShouldContain("2 stores are configured");
        ex.Message.ShouldContain("MicroKit.Messaging.IntegrationTests.SecondModule");
        ex.Message.ShouldContain("PublishOptions.Store");
    }

    [DockerRequiredFact]
    public async Task WithTwoStores_AnUnboundHandler_FailsAtStart()
    {
        await using var host = await TwoStoreHost.CreateAsync(
            fixture.ConnectionString, bindSecondModule: false, handleSecondModule: true);

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"Handler '{typeof(SecondModuleHandler).FullName}' is bound to no store");
        ex.Message.ShouldContain("ForAssemblyContaining<SecondModuleHandler>()");
    }

    [DockerRequiredFact]
    public async Task AnAssemblyBoundToTwoStores_FailsAtStart()
    {
        await using var host = await TwoStoreHost.CreateAsync(fixture.ConnectionString, bindThisAssemblyToBoth: true);

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("Assembly 'MicroKit.Messaging.IntegrationTests' is bound to 2 stores");
    }

    /// <summary>The host of these tests: two stores on two databases of the suite's server.</summary>
    private sealed class TwoStoreHost : IAsyncDisposable
    {
        private readonly StoreDatabase _a;
        private readonly StoreDatabase _b;
        private readonly IHost _host;

        private TwoStoreHost(IHost host, StoreDatabase a, StoreDatabase b)
        {
            _host = host;
            _a = a;
            _b = b;
        }

        public IServiceProvider Provider => _host.Services;

        public TwoStoresRecorder Recorder => Provider.GetRequiredService<TwoStoresRecorder>();

        public static async Task<TwoStoreHost> CreateAsync(
            string server,
            bool bindSecondModule = true,
            bool handleSecondModule = false,
            bool bindThisAssemblyToBoth = false)
        {
            var a = await StoreDatabase.CreateAsync<TestMessagingDbContext>(server, o => new(o));
            var b = await StoreDatabase.CreateAsync<SecondMessagingDbContext>(server, o => new(o));

            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Services.AddSingleton<TwoStoresRecorder>();
            builder.Services.AddSingleton<SecondModuleRecorder>();
            builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(a.ConnectionString));
            builder.Services.AddDbContext<SecondMessagingDbContext>(o => o.UseNpgsql(b.ConnectionString));
            builder.Services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = "tests.two-stores";
                bus.Publishes<SecondModuleEvent>();
                bus.Publishes<StoreBEvent>();
                bus.AddHandler<PublishingStoreBHandler>();
                bus.AddHandler<CrossStorePublishingHandler>();
                if (handleSecondModule)
                {
                    bus.AddHandler<SecondModuleHandler>();
                }

                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>(store =>
                {
                    if (bindSecondModule)
                    {
                        store.ForAssemblyContaining<SecondModuleMarker>();
                    }

                    if (bindThisAssemblyToBoth)
                    {
                        store.ForAssemblyContaining<TwoStoresTests>();
                    }
                });
                bus.UseEntityFrameworkOutbox<SecondMessagingDbContext>(store => store.ForAssemblyContaining<TwoStoresTests>());
                bus.UseInMemoryTransport();
            });

            return new TwoStoreHost(builder.Build(), a, b);
        }

        public Task StartAsync() => _host.StartAsync();

        public async Task SeedInboxOfBAsync<THandler>(StoreBEvent message)
        {
            await using var scope = Provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SecondMessagingDbContext>();
            context.InboxMessages.Add(new InboxMessage
            {
                MessageId = MessageId.New(),
                ConsumerType = typeof(THandler).AssemblyQualifiedName!,
                EventType = typeof(StoreBEvent).AssemblyQualifiedName!,
                Payload = $"{{\"id\":\"{message.Id}\"}}",
                Status = InboxMessageStatus.Received,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                OccurredOnUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        public async Task DrainInboxOfBAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredKeyedService<IInboxProcessor>(StoreB).ProcessBatchAsync(10);
        }

        public async Task<List<OutboxMessage>> OutboxOfAAsync()
        {
            await using var context = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(_a.ConnectionString).Options);
            return await context.OutboxMessages.AsNoTracking().ToListAsync();
        }

        public async Task<List<OutboxMessage>> OutboxOfBAsync()
        {
            await using var context = new SecondMessagingDbContext(
                new DbContextOptionsBuilder<SecondMessagingDbContext>().UseNpgsql(_b.ConnectionString).Options);
            return await context.OutboxMessages.AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _host.StopAsync();
            }
            catch (InvalidOperationException)
            {
                // Never started.
            }

            _host.Dispose();
            await _a.DisposeAsync();
            await _b.DisposeAsync();
        }
    }
}

[MessageContract("tests.two-stores.b-event")]
public sealed record StoreBEvent(Guid Id) : IIntegrationEvent;

public sealed class TwoStoresRecorder
{
    public System.Collections.Concurrent.ConcurrentQueue<Guid> Handled { get; } = new();

    public System.Collections.Concurrent.ConcurrentQueue<Exception> Refusals { get; } = new();
}

/// <summary>A store-B handler publishing a type bound to store A, then saving its own context.</summary>
public sealed class PublishingStoreBHandler(IMessageBus bus, SecondMessagingDbContext store, TwoStoresRecorder recorder)
    : IMessageHandler<StoreBEvent>
{
    public async ValueTask HandleAsync(StoreBEvent message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Handled.Enqueue(message.Id);
        await bus.PublishAsync(new SecondModuleEvent(message.Id), ct: ct);
        await store.SaveChangesAsync(ct);
    }
}

/// <summary>A store-B handler that names store A explicitly, which a delivery of B refuses.</summary>
public sealed class CrossStorePublishingHandler(IMessageBus bus, SecondMessagingDbContext store, TwoStoresRecorder recorder)
    : IMessageHandler<StoreBEvent>
{
    public async ValueTask HandleAsync(StoreBEvent message, MessageContext context, CancellationToken ct = default)
    {
        try
        {
            await bus.PublishAsync(
                new SecondModuleEvent(message.Id),
                new PublishOptions { Store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>() },
                ct);
        }
        catch (IntegrationEventPublishException ex)
        {
            recorder.Refusals.Enqueue(ex);
        }

        await store.SaveChangesAsync(ct);
    }
}
