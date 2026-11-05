namespace MicroKit.Messaging.ArchitectureTests;

/// <summary>
/// Pins the complete public surface of the four shipped assemblies to an explicit allow-list
/// (#185).
/// </summary>
/// <remarks>
/// <para>
/// Before #185 <c>MicroKit.Messaging.Abstractions</c> published 54 types in one flat namespace, so
/// an application developer met the engine's internals — coordinators, processors, nine store
/// contracts, the mutable EF rows and the batch plumbing — instead of the seven types it uses, and
/// every internal change was a public break. The split is only worth anything if widening it again
/// costs a deliberate edit here, which is what these tests are for.
/// </para>
/// <para>
/// <b>Every namespace of every assembly is pinned, not just the two interesting ones.</b> A test
/// that watched only <c>MicroKit.Messaging</c> and <c>MicroKit.Messaging.Spi</c> would stay green
/// while a public type appeared in <c>.Processing</c>, <c>.Registry</c>, <c>.Options</c> or
/// <c>.Publishing</c> — and <c>.Processing</c> and <c>.Registry</c> expose nothing at all today,
/// which is precisely the property most easily lost by accident.
/// </para>
/// <para>
/// <b>Why <see cref="Type.IsVisible"/> and not <c>IsPublic &amp;&amp; !IsNested</c>.</b>
/// <c>IsVisible</c> is true for a public type nested in a public one — which is reachable surface
/// and must be pinned — and false for a public type nested in an internal one, which is not
/// reachable and must not be. The pair of flags gets the first case wrong.
/// </para>
/// <para>
/// <b>Why <c>Types.InAssembly(...).GetTypes()</c> and not <c>Assembly.GetTypes()</c>.</b>
/// <c>IsTrimmable</c> is set repo-wide, so a direct call raises IL2026, which is an error under
/// Release. Keeping the reflection inside NetArchTest avoids suppressing a trimming diagnostic in
/// our own code.
/// </para>
/// <para>
/// Assertions run in both directions: an unexpected type fails, and an allow-list entry that no
/// longer exists fails too. A snapshot that only caught additions would quietly rot into a list of
/// types that are not there any more.
/// </para>
/// </remarks>
public sealed class PublicSurfaceTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(IIntegrationEvent).Assembly;
    private static readonly Assembly CoreAssembly = typeof(MessagingBuilder).Assembly;
    private static readonly Assembly EfCoreAssembly = typeof(ModelBuilderExtensions).Assembly;
    private static readonly Assembly MediatRGlueAssembly = typeof(MessagingMediatRExtensions).Assembly;

    /// <summary>
    /// The application surface: what a developer who publishes and handles integration events
    /// touches, and nothing else. Eight types from Abstractions, four composition-root types from
    /// Core.
    /// </summary>
    /// <remarks>
    /// This is the allow-list issue #185 asks for by name. It spans two assemblies because
    /// <c>MicroKit.Messaging</c> is the <c>RootNamespace</c> of both the contracts package and the
    /// implementation package.
    /// </remarks>
    [Fact]
    public void TheApplicationSurface_MatchesTheSnapshot()
    {
        string[] expected =
        [
            // Abstractions — the contracts an application names. InboxDependencyUnavailableException
            // is here, not in Spi: a handler is its ONLY source, and the engine catches it.
            "IIntegrationEvent",
            "IIntegrationEventPublisher",
            "InboxDependencyUnavailableException",
            "IMessageHandler`1",
            "IntegrationEventAttribute",
            "IntegrationEventConfigurationException",
            "IntegrationEventPublishException",
            "MessageId",
            // Core — composition and diagnostics.
            "IntegrationEventServiceCollectionExtensions",
            "MessagingBuilder",
            "MessagingDiagnostics",
            "ServiceCollectionExtensions",
        ];

        var actual = VisibleTypeNamesIn("MicroKit.Messaging", AbstractionsAssembly, CoreAssembly);

        // CONTROL: the query resolves real types, so the comparison below is not two empty sets.
        actual.ShouldNotBeEmpty("the root namespace must expose the application surface");

        actual.ShouldBe(
            expected.Order(StringComparer.Ordinal).ToList(),
            "the application surface of MicroKit.Messaging is pinned by #185: an application " +
            "developer must meet these types and no others. Adding one is a deliberate act — " +
            "update this list and say why in the PR, and consider whether the type belongs in " +
            "MicroKit.Messaging.Spi instead. Do not delete the assertion.");
    }

    /// <summary>
    /// The SPI: what an author of a store, a transport or a dispatcher implements. Separate from the
    /// application surface so that neither can grow by accident into the other.
    /// </summary>
    [Fact]
    public void TheSpiSurface_MatchesTheSnapshot()
    {
        string[] expected =
        [
            // Transport and the wire form.
            "IMessageTransport", "MessageEnvelope", "IEnvelopeReceiver", "EnvelopeReceiveResult",
            "IMessageSerializer",
            // Dispatch.
            "IOutboxDispatcher", "OutboxDispatcherKeys",
            // Stores, split by consumer (ISP).
            "IOutboxWriter", "IOutboxProcessorStore", "IOutboxAdminStore", "IOutboxRetentionStore",
            "IInboxWriter", "IInboxProcessorStore", "IInboxSettlementStore", "IInboxAdminStore",
            "IInboxRetentionStore",
            // The staging writer.
            "IIntegrationEventWriter", "IntegrationEventWriteResult",
            // The rows and the types the stores exchange.
            "OutboxMessage", "InboxMessage", "OutboxClaim", "InboxClaim", "OutboxOutcome",
            "InboxOutcome", "InboxMessageKey",
            // Value objects carried on a row.
            "CorrelationId", "CausationId",
            // Row and outcome state.
            "MessageKind", "InboxWriteResult", "OutboxMessageStatus", "InboxMessageStatus",
            "OutboxOutcomeKind", "InboxOutcomeKind",
            // The exceptions a provider raises across the seam, for the engine to classify.
            "OutboxPayloadException", "OutboxTransportUnavailableException",
            "OutboxConfigurationException", "InboxPayloadException", "InboxConfigurationException",
        ];

        var actual = VisibleTypeNamesIn("MicroKit.Messaging.Spi", AbstractionsAssembly, CoreAssembly);

        // CONTROL: the SPI namespace is populated, so a passing comparison means agreement.
        actual.ShouldNotBeEmpty("MicroKit.Messaging.Spi must expose the store and transport contracts");

        actual.ShouldBe(
            expected.Order(StringComparer.Ordinal).ToList(),
            "MicroKit.Messaging.Spi is the contract a store or transport author implements (#185). " +
            "It is public API: removing or renaming a member breaks every provider. Update this " +
            "list deliberately rather than deleting the assertion.");
    }

    /// <summary>
    /// Abstractions exposes the application surface and the SPI, and nothing in any third
    /// namespace.
    /// </summary>
    [Fact]
    public void Abstractions_ExposesNoOtherNamespace()
    {
        VisibleNamespacesIn(AbstractionsAssembly).ShouldBe(
            ["MicroKit.Messaging", "MicroKit.Messaging.Spi"],
            "the contracts package is the application surface plus the SPI (#185); a third public " +
            "namespace means one of the two grew a sub-namespace nobody decided on");
    }

    /// <summary>
    /// Core's public surface is composition only. <c>.Processing</c> and <c>.Registry</c> must stay
    /// absent: the processors, coordinators, batch results, metrics and registries all became
    /// internal with #185.
    /// </summary>
    [Fact]
    public void Core_ExposesCompositionOnly()
    {
        VisibleNamespacesIn(CoreAssembly).ShouldBe(
            ["MicroKit.Messaging", "MicroKit.Messaging.Options", "MicroKit.Messaging.Publishing"],
            "Core publishes composition and options only (#185). MicroKit.Messaging.Processing and " +
            "MicroKit.Messaging.Registry must expose NOTHING — a name appearing there means a " +
            "processor, coordinator, batch result, metric or registry went public again");

        VisibleTypeNamesIn("MicroKit.Messaging.Options", CoreAssembly).ShouldBe(
            ["InboxProcessorOptions", "OutboxProcessorOptions"]);

        VisibleTypeNamesIn("MicroKit.Messaging.Publishing", CoreAssembly).ShouldBe(
            ["IntegrationEventContractBuilder", "IntegrationEventSubscriptionBuilder"],
            "only the two fluent builders are public here; the registry, the registrations and the " +
            "subscription collections became internal with #185");
    }

    /// <summary>The two optional integration packages, pinned in full.</summary>
    [Fact]
    public void TheIntegrationPackages_MatchTheSnapshot()
    {
        VisibleNamespacesIn(EfCoreAssembly).ShouldBe(["MicroKit.Messaging.EntityFrameworkCore"]);
        VisibleTypeNamesIn("MicroKit.Messaging.EntityFrameworkCore", EfCoreAssembly).ShouldBe(
            [
                "InboxMessageConfiguration",
                "MessagingBuilderExtensions",
                "ModelBuilderExtensions",
                "OutboxMessageConfiguration",
            ],
            "the EF Core package exposes the two entity configurations and the two registration " +
            "entry points; the stores themselves are internal");

        VisibleNamespacesIn(MediatRGlueAssembly).ShouldBe(
            ["MicroKit.Messaging.MediatR.DependencyInjection"]);
        VisibleTypeNamesIn("MicroKit.Messaging.MediatR.DependencyInjection", MediatRGlueAssembly)
            .ShouldBe(
                ["MessagingMediatRExtensions"],
                "the glue is one registration method; the sink, the routing dispatcher and the " +
                "cascade publisher are internal");
    }

    /// <summary>
    /// The visible type names declared in <paramref name="ns"/> — compared by exact, ordinal
    /// namespace match.
    /// </summary>
    /// <remarks>
    /// Deliberately not NetArchTest's <c>ResideInNamespace</c>, which is a <b>prefix</b> match:
    /// asking it for <c>MicroKit.Messaging</c> also returns <c>MicroKit.Messaging.Spi</c>,
    /// <c>.Options</c> and <c>.Publishing</c>, so the application-surface snapshot would silently
    /// absorb every type in the module.
    /// </remarks>
    private static List<string> VisibleTypeNamesIn(string ns, params Assembly[] assemblies) =>
        assemblies
            .SelectMany(a => Types.InAssembly(a).GetTypes())
            .Where(t => t.IsVisible && string.Equals(t.Namespace, ns, StringComparison.Ordinal))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The distinct namespaces in which <paramref name="assembly"/> exposes anything.</summary>
    private static List<string> VisibleNamespacesIn(Assembly assembly) =>
        Types.InAssembly(assembly)
            .GetTypes()
            .Where(t => t.IsVisible)
            .Select(t => t.Namespace ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}
