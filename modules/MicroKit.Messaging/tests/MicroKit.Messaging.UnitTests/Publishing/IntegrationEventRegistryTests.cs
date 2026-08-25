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

        registry.Resolve(typeof(ConstatRecorded)).Source.ShouldBe("/saasbtp/safety");
        registry.Resolve(typeof(WorkspaceCreated)).Source.ShouldBe("/saasbtp/access");
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

    private static ServiceProvider BuildPublishingProvider(
        params (string Source, Action<IntegrationEventContractBuilder> Configure)[] modules)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        foreach (var (source, configure) in modules)
        {
            services.AddIntegrationEventContracts(source, configure);
        }

        services.AddMicroKitMessaging().AddIntegrationEventPublishing();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
