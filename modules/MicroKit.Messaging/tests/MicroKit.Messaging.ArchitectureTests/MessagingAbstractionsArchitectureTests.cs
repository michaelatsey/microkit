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
