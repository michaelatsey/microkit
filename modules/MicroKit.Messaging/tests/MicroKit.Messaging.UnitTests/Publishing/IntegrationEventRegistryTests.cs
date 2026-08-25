namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Publishing;
using Microsoft.Extensions.Hosting;

/// <summary>Registry composition — the contract surface of the whole application.</summary>
public sealed class IntegrationEventRegistryTests
{
    /// <summary>
    /// The defect this shape exists to fix. With a per-application <c>Source</c> registered from a
    /// per-module call site, the last module to register overwrote every other module's identity —
    /// silently, and visible only as a wrong source on the wire.
    /// </summary>
    [Fact]
    public void Registry_KeepsEachModulesOwnSource()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()),
            ("/saasbtp/access", e => e.Publishes<WorkspaceCreated>()));

        registry.ResolveContract(typeof(ConstatRecorded)).Source.ShouldBe("/saasbtp/safety");
        registry.ResolveContract(typeof(WorkspaceCreated)).Source.ShouldBe("/saasbtp/access");
    }

    /// <summary>
    /// Detectable only because every module composes into one registry. Per-module registries — the
    /// shape a per-module options object would have produced — could not see this at all, and two
    /// payloads would go out under one name until a consumer failed to deserialize.
    /// </summary>
    [Fact]
    public void Registry_WhenTwoModulesClaimOneContractName_IsRejected()
    {
        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => IntegrationEventContractFixtures.Registry(
                ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()),
                ("/saasbtp/access", e => e.Publishes<ImpostorEvent>())));

        exception.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
        exception.Message.ShouldContain(nameof(ImpostorEvent));

        // Both modules are named. A type name alone leaves an operator grepping a monolith for the
        // registration; the source is the half that says which composition root to open.
        exception.Message.ShouldContain("/saasbtp/safety");
        exception.Message.ShouldContain("/saasbtp/access");
    }

    [Fact]
    public void Registry_WhenOneTypeIsDeclaredTwice_IsRejected()
    {
        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => IntegrationEventContractFixtures.Registry(
                ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()),
                ("/saasbtp/safety-again", e => e.Publishes<ConstatRecorded>())));

        exception.Message.ShouldContain("declared twice");
    }

    [Fact]
    public void Publishes_WhenEventHasNoAttribute_IsRejectedAtRegistration()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => services.AddIntegrationEventContracts(
                "/saasbtp/safety", e => e.Publishes<NeverRegistered>()));

        exception.Message.ShouldContain(nameof(NeverRegistered));
    }

    /// <summary>
    /// A blank contract name is rejected the moment the attribute is read — and it is the empty
    /// case, not the null one, that this guard is for.
    /// </summary>
    /// <remarks>
    /// Null fails somewhere regardless; an empty string is a perfectly serviceable dictionary key.
    /// Unguarded it binds, resolves, and goes out on the wire as an event published under no
    /// identity at all — matchable only by a consumer that made the same mistake. The guard lives on
    /// <c>IntegrationEventAttribute</c> rather than in either builder, so one check covers both
    /// directions; these two tests are what say so, one per builder.
    /// </remarks>
    [Fact]
    public void Publishes_WhenContractNameIsEmpty_IsRejectedAtRegistration()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentException>(
            () => services.AddIntegrationEventContracts(
                "/saasbtp/safety", e => e.Publishes<BlankContractName>()));

        exception.ParamName.ShouldBe("contractName");
    }

    [Fact]
    public void Consumes_WhenContractNameIsWhitespace_IsRejectedAtRegistration()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentException>(
            () => services.AddIntegrationEventSubscriptions(
                e => e.Consumes<WhitespaceContractName>()));

        exception.ParamName.ShouldBe("contractName");
    }

    /// <summary>
    /// The snapshot. A contract name is public API: renaming one must fail the build, not silently
    /// strand every consumer subscribed to the old name.
    /// </summary>
    [Fact]
    public void Registry_ContractSurface_MatchesTheSnapshot()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()),
            ("/saasbtp/access", e => e.Publishes<WorkspaceCreated>()));

        registry.ContractNames.ShouldBe([
            "saasbtp.access.workspace-created.v1",
            "saasbtp.safety.constat-recorded.v1",
        ]);
        registry.Count.ShouldBe(2);
    }

    /// <summary>
    /// The registry's composition is validated <b>at startup</b>, not on first publication.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it guards against is subtle. A factory-built singleton is constructed on first resolve,
    /// which on this path is the first publication — inside a notification handler, inside a
    /// transaction. The exception would roll that transaction back and be classified as a transient
    /// failure, retrying forever against a duplicate contract name no retry can fix.
    /// </para>
    /// <para>
    /// Asserted by driving the hosted service directly rather than by starting a host. Starting one
    /// would also start the four messaging workers <c>AddMicroKitMessaging</c> registers, which
    /// need stores this test has no reason to wire — so the assertion would fail for reasons
    /// unrelated to what it claims. An assertion that cannot run is worse than none.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RegistryValidator_OnStart_FailsOnADuplicatedContractName()
    {
        await using var provider = BuildPublishingProvider(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()),
            ("/saasbtp/access", e => e.Publishes<ImpostorEvent>()));

        var validator = provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();

        var exception = await Should.ThrowAsync<IntegrationEventConfigurationException>(
            async () => await validator.StartAsync(CancellationToken.None));

        exception.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
    }

    /// <summary>
    /// The companion to the test above: eager validation only happens if the validator is actually
    /// registered as a hosted service, and nothing else in the suite would notice its removal.
    /// </summary>
    [Fact]
    public async Task AddIntegrationEventPublishing_RegistersTheValidatorAsAHostedService()
    {
        await using var provider = BuildPublishingProvider(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));

        provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RegistryValidator_OnStart_SucceedsWhenCompositionIsValid()
    {
        await using var provider = BuildPublishingProvider(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()),
            ("/saasbtp/access", e => e.Publishes<WorkspaceCreated>()));

        var validator = provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();

        await Should.NotThrowAsync(async () => await validator.StartAsync(CancellationToken.None));
    }

    // ---------------------------------------------------------------------------------------
    // The reverse direction: ContractName -> local CLR type.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The property step 3 rests on. A consumer holds a name and a payload and nothing else; it
    /// does not have the producer's assembly, so <c>Type.GetType(assemblyQualifiedName)</c> cannot
    /// give it something to deserialize into.
    /// </summary>
    [Fact]
    public void Registry_ResolvesAContractNameBackToItsLocalType()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));

        var contractName = registry.ResolveContract(typeof(ConstatRecorded)).ContractName;

        registry.TryResolveLocalType(contractName, out var eventType).ShouldBeTrue();
        eventType.ShouldBe(typeof(ConstatRecorded));
        registry.ResolveLocalType(contractName).ShouldBe(typeof(ConstatRecorded));
    }

    /// <summary>
    /// Publishing a contract binds its name, so a modular monolith routes its own contracts without
    /// a second declaration. Deleting the producer-side write to the name index must fail here.
    /// </summary>
    [Fact]
    public void Registry_PublishingAContractMakesItResolvable()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/access", e => e.Publishes<WorkspaceCreated>()));

        registry
            .TryResolveLocalType("saasbtp.access.workspace-created.v1", out var eventType)
            .ShouldBeTrue();
        eventType.ShouldBe(typeof(WorkspaceCreated));
    }

    /// <summary>
    /// The remote-consumer case, and the load-bearing test for keeping the two registrations apart:
    /// a consumed contract resolves by name yet never enters the <b>published</b> surface. It has
    /// no source — this process did not emit it — so offering to publish it would be a lie the
    /// wire would carry.
    /// </summary>
    [Fact]
    public void Registry_ResolvesAConsumedContractThatIsNotPublishedHere()
    {
        var registry = IntegrationEventContractFixtures.ConsumingRegistry(
            e => e.Consumes<InvoiceSettled>());

        registry.ResolveLocalType("partner.billing.invoice-settled.v1")
            .ShouldBe(typeof(InvoiceSettled));

        registry.ContractNames.ShouldBeEmpty();
        registry.Count.ShouldBe(0);
        Should.Throw<IntegrationEventConfigurationException>(
            () => registry.ResolveContract(typeof(InvoiceSettled)));
    }

    /// <summary>
    /// The consumer-side counterpart to the publish snapshot. In a service that only consumes,
    /// nothing else catches a renamed <c>[IntegrationEvent]</c> on a local type: the old name
    /// simply stops arriving.
    /// </summary>
    [Fact]
    public void Registry_SubscriptionSurface_MatchesTheSnapshot()
    {
        var registry = IntegrationEventContractFixtures.ConsumingRegistry(
            e => e.Consumes<InvoiceSettled>(),
            e => e.Consumes<WorkspaceCreated>());

        registry.SubscribedContractNames.ShouldBe([
            "partner.billing.invoice-settled.v1",
            "saasbtp.access.workspace-created.v1",
        ]);
    }

    /// <summary>
    /// A contract made resolvable by publishing it is not a declared subscription. Keeping the two
    /// surfaces separate is what lets each be snapshot-tested for what it actually claims.
    /// </summary>
    [Fact]
    public void Registry_PublishedContractsAreNotCountedAsSubscriptions()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));

        registry.SubscribedContractNames.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // Collisions across the two registrations.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The monolith case: module A publishes a contract and module B, in the same process,
    /// declares it consumes it. Rejecting that would force B to know whether its producer happens
    /// to be in-process — the exact knowledge a stable contract name exists to remove.
    /// </summary>
    [Fact]
    public void Registry_WhenAPublisherAndAConsumerDeclareTheSameType_IsAccepted()
    {
        var registry = IntegrationEventContractFixtures.MixedRegistry(
            [("/saasbtp/safety", e => e.Publishes<ConstatRecorded>())],
            e => e.Consumes<ConstatRecorded>());

        registry.ResolveLocalType("saasbtp.safety.constat-recorded.v1")
            .ShouldBe(typeof(ConstatRecorded));
        registry.ResolveContract(typeof(ConstatRecorded)).Source.ShouldBe("/saasbtp/safety");
        registry.Count.ShouldBe(1);
    }

    /// <summary>
    /// The collision a separate consumer-side registry could not see at all. A name resolves to one
    /// local type: a message is deserialized once, before any fan-out, so a rival claimant could
    /// only be honoured by picking arbitrarily — and structurally-compatible JSON would produce a
    /// plausible wrong object rather than an error.
    /// </summary>
    [Fact]
    public void Registry_WhenAConsumerMirrorTypeClaimsAPublishedName_IsRejected()
    {
        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => IntegrationEventContractFixtures.MixedRegistry(
                [("/saasbtp/safety", e => e.Publishes<ConstatRecorded>())],
                e => e.Consumes<ImpostorEvent>()));

        exception.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
        exception.Message.ShouldContain(nameof(ConstatRecorded));
        exception.Message.ShouldContain(nameof(ImpostorEvent));

        // The incumbent publishes, so it has a module to name.
        exception.Message.ShouldContain("/saasbtp/safety");
    }

    /// <summary>
    /// The same collision between two consumers, where <b>neither</b> claimant has a module to
    /// name — and the message invents nothing.
    /// </summary>
    /// <remarks>
    /// A subscription carries no <c>Source</c> by construction, so this message is two type names
    /// and no module. That is the asymmetry working as designed, not a gap: the alternative is a
    /// nominal source, which is a false value in the one field that has to stay true. Asserted
    /// rather than assumed, so a later "improvement" that fabricates one fails here.
    /// </remarks>
    [Fact]
    public void Registry_WhenTwoConsumersClaimOneNameWithDifferentTypes_IsRejected()
    {
        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => IntegrationEventContractFixtures.ConsumingRegistry(
                e => e.Consumes<ConstatRecorded>(),
                e => e.Consumes<ImpostorEvent>()));

        exception.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
        exception.Message.ShouldContain(nameof(ImpostorEvent));
        exception.Message.ShouldNotContain("from '");
    }

    /// <summary>Two modules consuming one contract through the same type is nominal.</summary>
    [Fact]
    public void Registry_WhenTwoConsumersShareOneType_IsAccepted()
    {
        var registry = IntegrationEventContractFixtures.ConsumingRegistry(
            e => e.Consumes<InvoiceSettled>(),
            e => e.Consumes<InvoiceSettled>());

        registry.ResolveLocalType("partner.billing.invoice-settled.v1")
            .ShouldBe(typeof(InvoiceSettled));
        registry.SubscribedContractNames.ShouldBe(["partner.billing.invoice-settled.v1"]);
    }

    [Fact]
    public void Consumes_WhenEventHasNoAttribute_IsRejectedAtRegistration()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => services.AddIntegrationEventSubscriptions(e => e.Consumes<NeverRegistered>()));

        exception.Message.ShouldContain(nameof(NeverRegistered));
    }

    // ---------------------------------------------------------------------------------------
    // An unknown contract name.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryResolveLocalType_WhenContractNameIsUnknown_ReturnsFalse()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));

        registry.TryResolveLocalType("nobody.knows.this.v1", out var eventType).ShouldBeFalse();
        eventType.ShouldBeNull();
    }

    /// <summary>
    /// Permanent, not transient: an unbound name cannot become bound without a redeploy. The
    /// message has to name both the contract and the call that fixes it, because the operator
    /// reading it is holding a dead-lettered row and nothing else.
    /// </summary>
    [Fact]
    public void ResolveLocalType_WhenContractNameIsUnknown_ThrowsAndNamesTheContractName()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));

        var exception = Should.Throw<IntegrationEventConfigurationException>(
            () => registry.ResolveLocalType("nobody.knows.this.v1"));

        exception.Message.ShouldContain("nobody.knows.this.v1");
        exception.Message.ShouldContain("Consumes");
    }

    /// <summary>
    /// A contract name is a wire identity, not display text. A case-insensitive or
    /// culture-sensitive match would collapse two distinct contracts onto one local type and
    /// deserialize the wrong shape without raising anything.
    /// </summary>
    /// <remarks>
    /// <b>Two candidates, because one does not pin the comparer.</b> Case alone separates
    /// <c>Ordinal</c> from <c>OrdinalIgnoreCase</c> and from nothing else —
    /// <c>StringComparer.InvariantCulture</c> is case-sensitive and passes a case-only assertion
    /// unchanged, so a switch to it would go unnoticed. The second candidate is the one that
    /// catches it: U+00AD SOFT HYPHEN is <i>collation-ignorable</i>, so a culture-sensitive
    /// dictionary finds the registered entry through it and an ordinal one does not. It is also the
    /// realistic corruption rather than an exotic one — these names are full of real hyphens, and a
    /// soft hyphen arrives by copy-paste out of anything that renders justified text.
    /// </remarks>
    [Fact]
    public void ResolveLocalType_IsOrdinal_AndDoesNotMatchOnCaseOrCulture()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));

        // Separates Ordinal from OrdinalIgnoreCase.
        registry
            .TryResolveLocalType("SAASBTP.SAFETY.CONSTAT-RECORDED.V1", out _)
            .ShouldBeFalse();
        registry
            .TryResolveLocalType("saasbtp.safety.Constat-Recorded.v1", out _)
            .ShouldBeFalse();

        // Separates Ordinal from InvariantCulture / CurrentCulture, which the two above cannot:
        // both of those resolve this name to ConstatRecorded.
        registry
            .TryResolveLocalType("saasbtp.safety.constat-\u00ADrecorded.v1", out _)
            .ShouldBeFalse();
    }

    /// <summary>
    /// Pins the deferral: the registry hands back a <see cref="Type"/>, and the serializer takes an
    /// assembly-qualified name, so the bridge step 3 will use is a string round trip. It is lossless
    /// only because the assembly is loaded by the time we hold the type — assert it rather than
    /// assume it, since the alternative is an additive overload nobody has needed yet.
    /// </summary>
    [Fact]
    public void ResolvedLocalType_RoundTripsThroughTheSerializer()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>()));
        var serializer = BuildSerializer();
        var payload = serializer.Serialize(new ConstatRecorded(Guid.NewGuid()));

        var eventType = registry.ResolveLocalType("saasbtp.safety.constat-recorded.v1");
        var deserialized = serializer.Deserialize(payload, eventType.AssemblyQualifiedName!);

        deserialized.ShouldBeOfType<ConstatRecorded>();
    }

    // ---------------------------------------------------------------------------------------
    // Composition: the boot-time guarantee reaches a service that only consumes.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The collision the validator must catch on the consuming side, in a service composed the way
    /// a publishing one is.
    /// </summary>
    [Fact]
    public async Task RegistryValidator_OnStart_FailsOnASubscriptionCollision()
    {
        await using var provider = BuildPublishingProvider(
            [("/saasbtp/safety", e => e.Publishes<ConstatRecorded>())],
            [e => e.Consumes<ImpostorEvent>()]);

        var validator = provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();

        var exception = await Should.ThrowAsync<IntegrationEventConfigurationException>(
            async () => await validator.StartAsync(CancellationToken.None));

        exception.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
    }

    /// <summary>
    /// A service that only consumes gets the same boot-time guarantee as one that publishes.
    /// </summary>
    /// <remarks>
    /// Without <c>AddIntegrationEventConsumption()</c> this test cannot even find a validator to
    /// drive: the registry would be composed lazily on first resolve, which on a drain path is
    /// inside a handler, inside a transaction, where a collision is misclassified as transient and
    /// retried forever against something no retry can fix. That is exactly what the validator
    /// exists to prevent, and a consumer needs it as much as a producer.
    /// </remarks>
    [Fact]
    public async Task ConsumerOnlyComposition_OnStart_FailsOnASubscriptionCollision()
    {
        await using var provider = BuildConsumingProvider(
            e => e.Consumes<ConstatRecorded>(),
            e => e.Consumes<ImpostorEvent>());

        var validator = provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();

        var exception = await Should.ThrowAsync<IntegrationEventConfigurationException>(
            async () => await validator.StartAsync(CancellationToken.None));

        exception.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
        exception.Message.ShouldContain(nameof(ImpostorEvent));
    }

    [Fact]
    public async Task ConsumerOnlyComposition_OnStart_SucceedsWhenCompositionIsValid()
    {
        await using var provider = BuildConsumingProvider(e => e.Consumes<InvoiceSettled>());

        var registry = provider.GetRequiredService<IntegrationEventRegistry>();
        registry.ResolveLocalType("partner.billing.invoice-settled.v1")
            .ShouldBe(typeof(InvoiceSettled));

        var validator = provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();

        await Should.NotThrowAsync(async () => await validator.StartAsync(CancellationToken.None));
    }

    /// <summary>
    /// A service that both publishes and consumes calls both entry points and must get ONE registry
    /// and ONE validator, whichever order they are written in.
    /// </summary>
    /// <remarks>
    /// Both orders, because a single order proves nothing about the failure this guards: under a
    /// plain <c>Add</c> the second caller appends a rival descriptor, Microsoft DI resolves the
    /// last one, and which registry the boot validation ran against would depend on the order the
    /// two lines happened to be typed in.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishingAndConsumption_Together_RegisterOneRegistryAndOneValidator(
        bool publishingFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationEventContracts(
            "/saasbtp/safety", e => e.Publishes<ConstatRecorded>());
        services.AddIntegrationEventSubscriptions(e => e.Consumes<InvoiceSettled>());

        var messaging = services.AddMicroKitMessaging();
        if (publishingFirst)
        {
            messaging.AddIntegrationEventPublishing().AddIntegrationEventConsumption();
        }
        else
        {
            messaging.AddIntegrationEventConsumption().AddIntegrationEventPublishing();
        }

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        provider.GetServices<IHostedService>()
            .OfType<IntegrationEventRegistryValidator>()
            .ShouldHaveSingleItem();

        var registry = provider.GetServices<IntegrationEventRegistry>().ShouldHaveSingleItem();
        provider.GetRequiredService<IntegrationEventRegistry>().ShouldBeSameAs(registry);

        registry.ContractNames.ShouldBe(["saasbtp.safety.constat-recorded.v1"]);
        registry.SubscribedContractNames.ShouldBe(["partner.billing.invoice-settled.v1"]);
    }

    private static IMessageSerializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMicroKitMessaging().AddInProcessTransport();

        return services.BuildServiceProvider().GetRequiredService<IMessageSerializer>();
    }

    private static ServiceProvider BuildConsumingProvider(
        params Action<IntegrationEventSubscriptionBuilder>[] consumers)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        foreach (var configure in consumers)
        {
            services.AddIntegrationEventSubscriptions(configure);
        }

        services.AddMicroKitMessaging().AddIntegrationEventConsumption();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ServiceProvider BuildPublishingProvider(
        params (string Source, Action<IntegrationEventContractBuilder> Configure)[] modules)
        => BuildPublishingProvider(modules, subscriptions: []);

    private static ServiceProvider BuildPublishingProvider(
        (string Source, Action<IntegrationEventContractBuilder> Configure)[] modules,
        Action<IntegrationEventSubscriptionBuilder>[] subscriptions)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        foreach (var (source, configure) in modules)
        {
            services.AddIntegrationEventContracts(source, configure);
        }

        foreach (var configure in subscriptions)
        {
            services.AddIntegrationEventSubscriptions(configure);
        }

        services.AddMicroKitMessaging().AddIntegrationEventPublishing();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
