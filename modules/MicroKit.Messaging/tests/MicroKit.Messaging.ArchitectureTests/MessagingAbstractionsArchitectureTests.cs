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
        // were retired when the publisher moved onto transport outbox rows. The argument
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
                        "integration-event table was retired onto transport outbox rows");
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
    /// No outbox dispatcher depends on <see cref="IInboxGuardStore"/> — nothing writes inbox rows on
    /// the <i>producing</i> side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This replaces <c>Core_DoesNotDependOnIInboxWriter</c>, which ADR-MSG-019 predicted would
    /// fail at exactly this step.</b> It did, and deliberately: it was written so that whoever
    /// built the receiving seam had to come back to the ADR rather than quietly reinstate an
    /// in-process producer. That has now happened — the receive pipeline lives in Core and writes
    /// rows through <see cref="IInboxGuardStore"/> (#188; <c>IInboxWriter</c> before it) — so the old
    /// assertion is retired rather than deleted, and the subject it was really guarding is stated
    /// here instead.
    /// </para>
    /// <para>
    /// <b>The subject was never "Core must not write inbox rows".</b> It was
    /// "no dispatcher writes inbox rows": the defect ADR-MSG-018 and ADR-MSG-019 spent two rewrites
    /// removing was a fan-out that wrote a <i>consumer's</i> row inside the <i>producer</i>,
    /// bypassing the wire and the contract name that make a consumer's own type resolvable. A row
    /// written from the receiving side, from an envelope that crossed a transport, is the opposite
    /// of that defect. This test therefore narrows to the dispatch path, where the regression would
    /// actually reappear.
    /// </para>
    /// <para>
    /// <b>Since #188 the dispatcher seam is internal to Core</b>, so it is selected by name and the
    /// glue is asserted to have none: <c>MicroKit.Messaging.MediatR</c> sends local messages through
    /// the public <c>ILocalMessageSender</c>, and <c>LocalOutboxDispatcher</c> delivers them through
    /// the receive pipeline — the receiving side, whose guard writes each consumer's row.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoOutboxDispatcherWritesInboxRows()
    {
        // CONTROL 1 — the dependency query. An emptiness assertion is worthless if the query can
        // never match anything: a misspelled name, or a matching rule that does not see interface
        // implementations, would make this permanently green and permanently useless. EfCoreAssembly
        // is known to depend on IInboxGuardStore (EfInboxStore implements it).
        Types.InAssembly(EfCoreAssembly)
            .That()
            .HaveDependencyOn(typeof(IInboxGuardStore).FullName)
            .GetTypes()
            .ShouldNotBeEmpty(
                "control assertion: if this is empty the dependency query below cannot detect " +
                "anything and the real assertion is vacuous");

        // CONTROL 2 — the selector. The assertion below is a CONJUNCTION, so it can go vacuous on
        // either side: a selector matching no dispatcher at all yields an empty set whatever the
        // dispatchers do. The original test needed one control; this one needs two.
        var dispatcher = Types.InAssembly(CoreAssembly)
            .That().HaveName("IOutboxDispatcher").And().ResideInNamespace("MicroKit.Messaging.Processing")
            .GetTypes()
            .ShouldHaveSingleItem("the dispatcher seam is found by name: it is internal since #188");

        Types.InAssembly(CoreAssembly)
            .That()
            .ImplementInterface(dispatcher)
            .GetTypes()
            .Count()
            .ShouldBe(2, "control assertion: Core holds the transport and the local dispatcher, or " +
                         "the assertion below selects nothing and proves nothing");

        Types.InAssembly(CoreAssembly)
            .That()
            .ImplementInterface(dispatcher)
            .And()
            .HaveDependencyOn(typeof(IInboxGuardStore).FullName)
            .GetTypes()
            .ShouldBeEmpty(
                "No dispatcher may write inbox rows from the dispatch path. Inbox rows are written by " +
                "the receive pipeline's guard, on the RECEIVING side (#188 D6; ADR-MSG-019). A " +
                "dispatcher that writes one is the in-process fan-out coming back. Update this test " +
                "AND the ADR rather than deleting the assertion");

        Types.InAssembly(MediatRGlueAssembly)
            .That()
            .HaveDependencyOn(dispatcher.FullName)
            .GetTypes()
            .ShouldBeEmpty("the glue sends local messages through the public SPI (#188 D8)");
    }

    /// <summary>
    /// The only <see cref="IMessageTransport"/> any MicroKit package ships is Core's in-memory one;
    /// broker transports ship from provider packages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test used to forbid every implementation. Its premise was that, without a receiving
    /// side, an in-process transport could only succeed silently on messages it never delivered,
    /// or exist purely to throw. #186 removed the premise: the in-memory transport is registered by
    /// <c>UseInMemoryTransport()</c> together with its consume loop, which runs every envelope
    /// through the receive pipeline, so a send that returns is a message that will be handled or
    /// dead-lettered.
    /// </para>
    /// <para>
    /// The rule that remains is narrow on purpose: exactly one implementation, internal, in Core,
    /// and none anywhere else. A second implementation — or a public one — is a decision, not a
    /// convenience. Every shipped assembly is asserted, so the glue and the EF Core package cannot
    /// contradict it either.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyCoreShipsAnIMessageTransport_AndItIsTheInternalInMemoryOne()
    {
        Assembly[] others = [AbstractionsAssembly, EfCoreAssembly, MediatRGlueAssembly];

        foreach (var assembly in others)
        {
            Types.InAssembly(assembly)
                .That()
                .ImplementInterface(typeof(IMessageTransport))
                .GetTypes()
                .Select(t => t.FullName)
                .ShouldBeEmpty(
                    $"{assembly.GetName().Name} must ship no IMessageTransport: broker transports " +
                    "ship from provider packages, and the in-memory one lives in Core.");
        }

        var core = Types.InAssembly(CoreAssembly)
            .That()
            .ImplementInterface(typeof(IMessageTransport))
            .GetTypes()
            .ToList();

        var only = core.ShouldHaveSingleItem("Core ships exactly one transport: the in-memory one");
        only.FullName.ShouldBe("MicroKit.Messaging.Transport.InMemoryTransport");
        only.IsPublic.ShouldBeFalse("the transport is reached through UseInMemoryTransport(), never named");
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

    /// <summary>
    /// The successor of the two coordinator rules (#188): the engine iterates stores through
    /// <see cref="IMessageStoreSet"/>, and only the composition root names the static set, so #191
    /// can put a dynamic set behind the same SPI without editing the engine.
    /// </summary>
    [Fact]
    public void Core_DependsOnTheStaticStoreSet_OnlyThroughIMessageStoreSet()
    {
        // Name-based: the static set is internal and this project has no InternalsVisibleTo.
        const string StaticSet = "MicroKit.Messaging.Stores.StaticMessageStoreSet";

        Types.InAssembly(CoreAssembly).GetTypes()
            .ShouldContain(t => t.FullName == StaticSet, "CONTROL: the rule names a type that exists");

        var permitted = new[] { StaticSet, "MicroKit.Messaging.ServiceCollectionExtensions" };

        var offenders = Types.InAssembly(CoreAssembly)
            .That()
            .HaveDependencyOn(StaticSet)
            .GetTypes()
            .Where(t => !permitted.Contains(t.FullName, StringComparer.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty(
            "only the composition root may name StaticMessageStoreSet; the engine iterates stores " +
            "through IMessageStoreSet, or the dynamic set of #191 cannot replace it");
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
