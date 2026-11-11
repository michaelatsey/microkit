namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Publishing;
using static MicroKit.Messaging.UnitTests.Publishing.IntegrationEventContractFixtures;

/// <summary>
/// Registry composition — the contract surface of the service, built from <c>bus.Publishes</c> and
/// the handlers (#187). The registry applies the contract rules itself, so these run without a host.
/// </summary>
public sealed class IntegrationEventRegistryTests
{
    // ---------------------------------------------------------------------------------------
    // The source and the published surface.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Registry_UsesTheServiceNameAsTheSourceOfEveryPublication()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>().Publishes<WorkspaceCreated>());

        registry.Source.ShouldBe(ServiceName);
    }

    /// <summary>
    /// The snapshot. A contract name is public API: renaming one must fail the build, not silently
    /// strand every consumer subscribed to the old name.
    /// </summary>
    [Fact]
    public void Registry_ContractSurface_MatchesTheSnapshot()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>().Publishes<WorkspaceCreated>());

        registry.ContractNames.ShouldBe([
            "saasbtp.access.workspace-created.v1",
            "saasbtp.safety.constat-recorded.v1",
        ]);
        registry.Count.ShouldBe(2);
    }

    [Fact]
    public void Publishes_SameTypeTwice_IsANoOp()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>().Publishes<ConstatRecorded>());

        registry.Count.ShouldBe(1);
    }

    /// <summary>Repeated calls add to one composition: one module each, one registry.</summary>
    [Fact]
    public void Registry_AcrossTwoAddMicroKitMessagingCalls_ComposesBothModules()
    {
        var registry = Registry([
            bus => bus.Publishes<ConstatRecorded>(),
            bus => bus.Publishes<WorkspaceCreated>().AddHandler<InvoiceSettledHandler>(),
        ]);

        registry.ContractNames.ShouldBe([
            "saasbtp.access.workspace-created.v1",
            "saasbtp.safety.constat-recorded.v1",
        ]);
        registry.SubscribedContractNames.ShouldBe(["partner.billing.invoice-settled.v1"]);
    }

    [Fact]
    public void Registry_CarriesTheContractNameAndVersion_ReadAtRegistration()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecordedV2>());

        registry.TryGetContract(typeof(ConstatRecordedV2), out var contract).ShouldBeTrue();
        contract.Name.ShouldBe("saasbtp.safety.constat-recorded");
        contract.Version.ShouldBe(2);
        contract.ContractName.ShouldBe("saasbtp.safety.constat-recorded.v2");
    }

    // ---------------------------------------------------------------------------------------
    // Contract rules — the analysis the registry applies.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Registry_WhenTwoPublicationsClaimOneWireType_IsRejected()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Registry(bus => bus.Publishes<ConstatRecorded>().Publishes<ImpostorEvent>()));

        ex.Message.ShouldContain("saasbtp.safety.constat-recorded.v1");
        ex.Message.ShouldContain(typeof(ConstatRecorded).FullName!);
        ex.Message.ShouldContain(typeof(ImpostorEvent).FullName!);
    }

    /// <summary>A collision across two modules is visible only to one composed registry.</summary>
    [Fact]
    public void Registry_WhenTwoModulesClaimOneWireType_IsRejected()
    {
        Should.Throw<IntegrationEventConfigurationException>(() => Registry([
            bus => bus.Publishes<ConstatRecorded>(),
            bus => bus.Publishes<ImpostorEvent>(),
        ]));
    }

    /// <summary>Collisions are judged on the wire type: two versions of one name are two contracts.</summary>
    [Fact]
    public void Registry_TwoVersionsOfOneNameOnTwoTypes_AreAccepted()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>().Publishes<ConstatRecordedV2>());

        registry.ContractNames.ShouldBe([
            "saasbtp.safety.constat-recorded.v1",
            "saasbtp.safety.constat-recorded.v2",
        ]);
    }

    /// <summary>
    /// One CLR type maps to exactly one contract — structurally: the attribute allows one usage per
    /// type and is not inherited, so a type cannot carry two and a subtype cannot borrow one.
    /// </summary>
    [Fact]
    public void MessageContractAttribute_AllowsOneContractPerType_AndIsNotInherited()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(MessageContractAttribute), typeof(AttributeUsageAttribute))!;

        usage.AllowMultiple.ShouldBeFalse();
        usage.Inherited.ShouldBeFalse();
    }

    [Fact]
    public void Registry_WhenAPublicationHasNoContract_IsRejectedAndNamesTheFix()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Registry(bus => bus.Publishes<NeverRegistered>()));

        ex.Message.ShouldContain(typeof(NeverRegistered).FullName!);
        ex.Message.ShouldContain("[MessageContract(");
    }

    [Fact]
    public void Registry_WhenAHandledTypeHasNoContract_IsRejectedAndNamesTheHandler()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Registry(bus => bus.AddHandler<NeverRegisteredHandler>()));

        ex.Message.ShouldContain(typeof(NeverRegistered).FullName!);
        ex.Message.ShouldContain(typeof(NeverRegisteredHandler).FullName!);
    }

    /// <summary>
    /// A blank name is not a missing contract but a broken attribute: it cannot be materialized.
    /// It is reported with every other problem rather than thrown at the declaration, so one bad
    /// type neither hides the others nor makes an assembly scan throw.
    /// </summary>
    [Fact]
    public void Publishes_WhenContractNameIsEmpty_IsRejectedAndNamesTheAttributesError()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Registry(bus => bus.Publishes<BlankContractName>()));

        ex.Message.ShouldContain($"'{typeof(BlankContractName).FullName}' carries a [MessageContract] that cannot be read");
    }

    [Fact]
    public void AddHandler_WhenTheHandledContractNameIsWhitespace_IsRejectedAndNamesTheAttributesError()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Registry(bus => bus.AddHandler<WhitespaceContractNameHandler>()));

        ex.Message.ShouldContain($"'{typeof(WhitespaceContractName).FullName}' carries a [MessageContract] that cannot be read");
        ex.Message.ShouldNotContain("has no [MessageContract]", Case.Sensitive);
    }

    /// <summary>Every problem is listed in one exception, numbered, rather than the first alone.</summary>
    [Fact]
    public void Registry_ListsEveryProblemInOneException()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(() => Registry(bus => bus
            .Publishes<NeverRegistered>()
            .Publishes<ConstatRecorded>()
            .Publishes<ImpostorEvent>()));

        ex.Message.ShouldContain("(2 problems)");
        ex.Message.ShouldContain("1. ");
        ex.Message.ShouldContain("2. ");
        ex.Message.ShouldContain(typeof(NeverRegistered).FullName!);
        ex.Message.ShouldContain(typeof(ImpostorEvent).FullName!);
    }

    [Fact]
    public void Registry_WhenTwoCallsSetDifferentServiceNames_IsRejected()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(() => Registry([
            bus => bus.ServiceName = "shop.orders",
            bus => bus.ServiceName = "shop.billing",
        ]));

        ex.Message.ShouldContain("'shop.orders'");
        ex.Message.ShouldContain("'shop.billing'");
    }

    [Fact]
    public void Registry_WhenTwoCallsSetTheSameServiceName_IsAccepted()
    {
        var registry = Registry([
            bus => bus.ServiceName = "shop.orders",
            bus => bus.ServiceName = "shop.orders",
        ]);

        registry.Source.ShouldBe("shop.orders");
    }

    // ---------------------------------------------------------------------------------------
    // The reverse direction: a wire type to this process's own type. A handler is its subscription.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Registry_PublishingAContractMakesItResolvable()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>());

        registry.ResolveLocalType("saasbtp.safety.constat-recorded.v1").ShouldBe(typeof(ConstatRecorded));
    }

    [Fact]
    public void Registry_ResolvesAHandledContractThatIsNotPublishedHere()
    {
        var registry = Registry(bus => bus.AddHandler<InvoiceSettledHandler>());

        registry.ResolveLocalType("partner.billing.invoice-settled.v1").ShouldBe(typeof(InvoiceSettled));
        registry.ContractNames.ShouldBeEmpty();
    }

    /// <summary>The subscription snapshot: what this service's endpoint is bound to.</summary>
    [Fact]
    public void Registry_SubscriptionSurface_MatchesTheSnapshot()
    {
        var registry = Registry(bus => bus
            .AddHandler<InvoiceSettledHandler>()
            .AddHandler<ConstatRecordedHandler>());

        registry.SubscribedContractNames.ShouldBe([
            "partner.billing.invoice-settled.v1",
            "saasbtp.safety.constat-recorded.v1",
        ]);
    }

    [Fact]
    public void Registry_PublishedContractsAreNotCountedAsSubscriptions()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>().AddHandler<InvoiceSettledHandler>());

        registry.SubscribedContractNames.ShouldBe(["partner.billing.invoice-settled.v1"]);
    }

    /// <summary>A service handling its own publication declares nothing twice.</summary>
    [Fact]
    public void Registry_WhenAPublicationIsAlsoHandled_IsAccepted()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>().AddHandler<ConstatRecordedHandler>());

        registry.ContractNames.ShouldBe(["saasbtp.safety.constat-recorded.v1"]);
        registry.SubscribedContractNames.ShouldBe(["saasbtp.safety.constat-recorded.v1"]);
    }

    [Fact]
    public void Registry_WhenAHandledMirrorTypeClaimsAPublishedName_IsRejected()
    {
        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => Registry(bus => bus.Publishes<ConstatRecorded>().AddHandler<ImpostorHandler>()));

        ex.Message.ShouldContain(typeof(ImpostorEvent).FullName!);
    }

    [Fact]
    public void Registry_WhenTwoHandlersOfOneTypeAreRegistered_TheContractIsSubscribedOnce()
    {
        var registry = Registry(bus => bus
            .AddHandler<ConstatRecordedHandler>()
            .AddHandler<SecondConstatRecordedHandler>());

        registry.SubscribedContractNames.ShouldBe(["saasbtp.safety.constat-recorded.v1"]);
    }

    [Fact]
    public void TryResolveLocalType_WhenContractNameIsUnknown_ReturnsFalse()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>());

        registry.TryResolveLocalType("saasbtp.safety.unknown.v1", out var eventType).ShouldBeFalse();
        eventType.ShouldBeNull();
    }

    [Fact]
    public void ResolveLocalType_WhenContractNameIsUnknown_ThrowsAndNamesTheContractName()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>());

        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => registry.ResolveLocalType("saasbtp.safety.unknown.v1"));

        ex.Message.ShouldContain("saasbtp.safety.unknown.v1");
        ex.Message.ShouldContain("bus.AddHandler<THandler>()");
    }

    [Fact]
    public void ResolveContract_WhenTheTypeIsNotPublished_ThrowsAndNamesTheDeclaration()
    {
        var registry = Registry(bus => bus.AddHandler<ConstatRecordedHandler>());

        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => registry.ResolveContract(typeof(ConstatRecorded)));

        ex.Message.ShouldContain("bus.Publishes<ConstatRecorded>()");
    }

    /// <summary>
    /// A wire type is an identity, not display text. The soft hyphen separates Ordinal from
    /// InvariantCulture / CurrentCulture, which the case variants alone cannot.
    /// </summary>
    [Fact]
    public void ResolveLocalType_IsOrdinal_AndDoesNotMatchOnCaseOrCulture()
    {
        var registry = Registry(bus => bus.Publishes<ConstatRecorded>());

        registry.TryResolveLocalType("SAASBTP.SAFETY.CONSTAT-RECORDED.V1", out _).ShouldBeFalse();
        registry.TryResolveLocalType("saasbtp.safety.Constat-Recorded.v1", out _).ShouldBeFalse();
        registry.TryResolveLocalType("saasbtp.safety.constat-­recorded.v1", out _).ShouldBeFalse();
    }

    [Fact]
    public void ResolvedLocalType_RoundTripsThroughTheSerializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = ServiceName;
            bus.Publishes<ConstatRecorded>();
        });
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IntegrationEventRegistry>();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var payload = serializer.Serialize(new ConstatRecorded(Guid.NewGuid()), typeof(ConstatRecorded));

        var eventType = registry.ResolveLocalType("saasbtp.safety.constat-recorded.v1");

        serializer.Deserialize(payload, eventType).ShouldBeOfType<ConstatRecorded>();
    }
}
