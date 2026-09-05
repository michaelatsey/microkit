namespace MicroKit.Messaging.ArchitectureTests;

public sealed class MessagingAbstractionsArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(IIntegrationEvent).Assembly;
    private static readonly Assembly CoreAssembly = typeof(MessagingBuilder).Assembly;
    private static readonly Assembly EfCoreAssembly = typeof(MessagingBuilderExtensions).Assembly;
    private static readonly Assembly MediatRGlueAssembly = typeof(MessagingMediatRExtensions).Assembly;

    // ---------------------------------------------------------------------------
    // Abstractions layer checks
    // ---------------------------------------------------------------------------

    [Fact]
    public void Abstractions_HasNoEfCoreDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoMediatRContractsDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR.Contracts")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoMediatRDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // Core layer checks (Ruling 8 + architecture requirements)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Core_HasNoEfCoreDependency()
    {
        Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Core_HasNoMediatRContractsDependency()
    {
        Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR.Contracts")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Core_HasNoBrokerDependency()
    {
        var result = Types.InAssembly(CoreAssembly).ShouldNot()
            .HaveDependencyOn("RabbitMQ.Client").GetResult();
        result.IsSuccessful.ShouldBeTrue("Core must not reference RabbitMQ.Client");

        var result2 = Types.InAssembly(CoreAssembly).ShouldNot()
            .HaveDependencyOn("Azure.Messaging.ServiceBus").GetResult();
        result2.IsSuccessful.ShouldBeTrue("Core must not reference Azure.Messaging.ServiceBus");

        var result3 = Types.InAssembly(CoreAssembly).ShouldNot()
            .HaveDependencyOn("Confluent.Kafka").GetResult();
        result3.IsSuccessful.ShouldBeTrue("Core must not reference Confluent.Kafka");
    }

    [Fact]
    public void Core_DoesNotContainTypeNamedMessageDispatcher()
    {
        // MessageDispatcher was eliminated by Ruling 5 in favour of the IOutboxDispatcher seam.
        // This test prevents accidental re-introduction.
        Types.InAssembly(CoreAssembly)
            .That()
            .HaveNameEndingWith("MessageDispatcher")
            .GetTypes()
            .ShouldBeEmpty("MessageDispatcher was eliminated — use IOutboxDispatcher instead");
    }

    [Fact]
    public void NoAssemblyStillCarriesTheDedicatedIntegrationEventTable()
    {
        // IntegrationEventMessage, IntegrationEventStatus and IntegrationEventMessageConfiguration
        // were retired when the publisher moved onto MessageKind.Contract outbox rows. The argument
        // that justified a separate table — that a discriminator INFERRED from the payload's CLR
        // type could feed integration events into the MediatR fan-out — no longer applies: the
        // nature of a row is DECLARED by its writer in a column, and both dispatchers switch on
        // that column rather than testing a type.
        //
        // Re-introducing any of them would restore two models for one notion, which is the state
        // this step existed to end. Named types rather than a suffix match, because the suffix
        // "Message" is legitimately carried by OutboxMessage and InboxMessage.
        //
        // One name per call, deliberately. NetArchTest's HaveName(params string[]) is a
        // CONJUNCTION — it selects types having ALL the given names, which is nothing, always. A
        // single call listing all three is therefore vacuously green whatever the assemblies
        // contain. Verified by mutation: adding "OutboxMessage" to such a list did not fail.
        string[] retired =
        [
            "IntegrationEventMessage",
            "IntegrationEventStatus",
            "IntegrationEventMessageConfiguration",
        ];

        foreach (var assembly in new[] { AbstractionsAssembly, CoreAssembly, EfCoreAssembly })
        {
            foreach (var name in retired)
            {
                Types.InAssembly(assembly)
                    .That()
                    .HaveName(name)
                    .GetTypes()
                    .ShouldBeEmpty(
                        $"{assembly.GetName().Name} must not reinstate {name}; the dedicated " +
                        "integration-event table was retired onto MessageKind.Contract rows");
            }
        }
    }

    [Fact]
    public void Core_DoesNotContainTypeNamedInProcessIntegrationDispatcher()
    {
        // The in-process fan-out was withdrawn by ADR-MSG-019: a Contract row now travels to a
        // transport as a MessageEnvelope, and the receiving side writes its own inbox rows. This
        // test prevents accidental re-introduction, on the MessageDispatcher precedent above.
        //
        // Re-introducing it would not merely duplicate the transport — it would restore a producer
        // that writes inbox rows on the PRODUCING side, which is the confusion the contract-name
        // indirection exists to remove.
        Types.InAssembly(CoreAssembly)
            .That()
            .HaveNameEndingWith("IntegrationDispatcher")
            .GetTypes()
            .ShouldBeEmpty(
                "the in-process fan-out was withdrawn (ADR-MSG-019) — a contract row goes to " +
                "IMessageTransport, and the receiver writes its own inbox rows");
    }

    /// <summary>
    /// Nothing in Core depends on <see cref="IInboxWriter"/> — the inbox has no producer in this
    /// release, and this is what proves it rather than asserting it in prose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After ADR-MSG-019 the ingestion half of the inbox is <i>unfed</i>, not deleted:
    /// <c>InboxProcessor</c>, the claim, the settlement and both retention workers are unchanged
    /// and still correct, but nothing in this package writes a row for them to drain. Prose says
    /// that; this test makes it checkable.
    /// </para>
    /// <para>
    /// <b>It is expected to fail when the receiving seam arrives</b>, and that is the point. Whoever
    /// builds it must come back to this assertion and to the ADR rather than quietly reinstating an
    /// in-process producer — which is exactly how the fan-out survived two rewrites that should
    /// have removed it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Core_DoesNotDependOnIInboxWriter()
    {
        // CONTROL FIRST. An emptiness assertion is worthless if the query can never match anything
        // — a misspelled name, or a matching rule that does not see interface implementations,
        // would make this test permanently green and permanently useless. EfCoreAssembly is known
        // to depend on IInboxWriter (EfInboxStore implements it), so this proves the query works
        // before the real assertion below rests on it.
        Types.InAssembly(EfCoreAssembly)
            .That()
            .HaveDependencyOn(typeof(IInboxWriter).FullName)
            .GetTypes()
            .ShouldNotBeEmpty(
                "control assertion: if this is empty the query below cannot detect anything and " +
                "the real assertion is vacuous");

        Types.InAssembly(CoreAssembly)
            .That()
            .HaveDependencyOn(typeof(IInboxWriter).FullName)
            .GetTypes()
            .ShouldBeEmpty(
                "the inbox has no producer in this release (ADR-MSG-019). If the envelope receiver " +
                "has landed, update this test AND the ADR rather than deleting the assertion");
    }

    /// <summary>
    /// No MicroKit package implements <see cref="IMessageTransport"/>, and none must.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <c>InProcessTransport</c> is the obvious missing piece and the reason this test exists.
    /// Until the receiving seam is built, such a type has exactly two possible behaviours and both
    /// are defects: returning normally is the silent success this module treats as blocking — the
    /// processor marks the row <c>Published</c>, <c>Published</c> is terminal, and the message is
    /// gone with nothing recording that no delivery happened — while throwing on every send makes
    /// it a type that exists only to fail, which someone will nevertheless register.
    /// </para>
    /// <para>
    /// Every shipped assembly is asserted, not only Core, because that is the claim being made:
    /// the docs, the CHANGELOG and <c>AddTransportDispatcher</c> all say "no implementation ships
    /// in <i>any</i> MicroKit package", and a test covering two of the four would leave the glue
    /// and the EF Core package free to contradict it.
    /// </para>
    /// <para>
    /// A transport ships from a broker provider package, or not at all. If this test fails, read
    /// the new type's delivery path before deleting the assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMicroKitPackageShipsAnIMessageTransportImplementation()
    {
        Assembly[] shipped =
            [AbstractionsAssembly, CoreAssembly, EfCoreAssembly, MediatRGlueAssembly];

        foreach (var assembly in shipped)
        {
            var implementations = Types.InAssembly(assembly)
                .That()
                .ImplementInterface(typeof(IMessageTransport))
                .GetTypes()
                .Select(t => t.FullName)
                .ToList();

            implementations.ShouldBeEmpty(
                $"{assembly.GetName().Name} must ship no IMessageTransport: with no receiver, an " +
                "in-process transport can only succeed silently on messages it never delivered, " +
                "or exist purely to throw. Broker providers supply the implementation.");
        }
    }

    [Fact]
    public void Core_HasNoMediatRDependency()
    {
        // ADR-MSG-002: Core stays MediatR-free. Only the glue (MicroKit.Messaging.MediatR)
        // is permitted to reference MediatR / MediatR.Contracts (ADR-MSG-009 carve-out).
        Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void AllAssemblies_HaveNoMediatRContractsDependency()
    {
        // ADR-MSG-009: the MediatR glue (MediatRGlueAssembly) is the ONLY Messaging package
        // permitted to reference MediatR.Contracts, so it is intentionally excluded here.
        // NOTE: MicroKit.Messaging.Testing does not exist yet (Phase 1, planned). When it is
        // implemented it MUST be added to this array (ADR-MSG-009 keeps Testing clean).
        foreach (var assembly in new[] { AbstractionsAssembly, CoreAssembly, EfCoreAssembly })
        {
            Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("MediatR.Contracts")
                .GetResult()
                .IsSuccessful
                .ShouldBeTrue($"{assembly.GetName().Name} must not reference MediatR.Contracts");
        }
    }

    // ---------------------------------------------------------------------------
    // Topology seam (ADR-MSG-002)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Core_DependsOnSharedDbOutboxCoordinator_OnlyThroughIOutboxCoordinator()
    {
        // ADR-MSG-002 defers a per-tenant topology to MicroKit.Messaging.Multitenancy, which will
        // supply its own IOutboxCoordinator without touching Core. That is only possible while
        // nothing inside Core reaches for the shared-DB implementation by name. The composition
        // root necessarily does — that is what a composition root is — and the type may of course
        // refer to itself.
        //
        // Name-based rather than typeof(): SharedDbOutboxCoordinator is internal and this project
        // has no InternalsVisibleTo, which is itself part of the point.
        const string Coordinator = "MicroKit.Messaging.Processing.SharedDbOutboxCoordinator";

        var permitted = new[]
        {
            Coordinator,
            "MicroKit.Messaging.ServiceCollectionExtensions",
        };

        var offenders = Types.InAssembly(CoreAssembly)
            .That()
            .HaveDependencyOn(Coordinator)
            .GetTypes()
            .Where(t => !permitted.Contains(t.FullName, StringComparer.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty(
            "only the composition root may name SharedDbOutboxCoordinator; everything else must " +
            "depend on IOutboxCoordinator, or the per-tenant topology cannot be added additively");
    }

    /// <summary>
    /// The inbox twin of the test above. <c>SharedDbInboxCoordinator</c> carried the same
    /// ADR-MSG-002 promise in its doc comment from the beginning but had no guard behind it, so
    /// the promise was enforceable on one side of the module and merely stated on the other.
    /// </summary>
    [Fact]
    public void Core_DependsOnSharedDbInboxCoordinator_OnlyThroughIInboxCoordinator()
    {
        const string Coordinator = "MicroKit.Messaging.Processing.SharedDbInboxCoordinator";

        var permitted = new[]
        {
            Coordinator,
            "MicroKit.Messaging.ServiceCollectionExtensions",
        };

        var offenders = Types.InAssembly(CoreAssembly)
            .That()
            .HaveDependencyOn(Coordinator)
            .GetTypes()
            .Where(t => !permitted.Contains(t.FullName, StringComparer.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty(
            "only the composition root may name SharedDbInboxCoordinator; everything else must " +
            "depend on IInboxCoordinator, or the per-tenant topology cannot be added additively");
    }

    // ---------------------------------------------------------------------------
    // MediatR glue layer checks (ADR-MSG-009: glue MAY reference MediatR / MediatR.Contracts;
    // it must still stay free of EF Core, ASP.NET Core, and broker dependencies)
    // ---------------------------------------------------------------------------

    [Fact]
    public void MediatRGlue_HasNoEfCoreDependency()
    {
        Types.InAssembly(MediatRGlueAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void MediatRGlue_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(MediatRGlueAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void MediatRGlue_HasNoBrokerDependency()
    {
        var brokers = new[] { "RabbitMQ.Client", "Azure.Messaging.ServiceBus", "Confluent.Kafka" };
        foreach (var broker in brokers)
        {
            Types.InAssembly(MediatRGlueAssembly)
                .ShouldNot()
                .HaveDependencyOn(broker)
                .GetResult()
                .IsSuccessful
                .ShouldBeTrue($"MediatR glue must not reference {broker}");
        }
    }

    // ---------------------------------------------------------------------------
    // EntityFrameworkCore layer checks
    // ---------------------------------------------------------------------------

    [Fact]
    public void EfCore_HasNoMediatRDependency()
    {
        Types.InAssembly(EfCoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void EfCore_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(EfCoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }
}
