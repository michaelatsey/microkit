// GetReferencedAssemblies() checks direct references only (assembly manifest). Transitive refs are not checked.

namespace MicroKit.Multitenancy.ArchitectureTests;

public sealed class AbstractionsDependencyTests
{
    // Anchor: TenantId lives only in Abstractions (not in Core, which shares the MicroKit.Multitenancy namespace).
    private static readonly Assembly AbstractionsAssembly = typeof(TenantId).Assembly;

    [Fact]
    public void Abstractions_DoesNotReferenceAspNetCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.AspNetCore")).ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.EntityFrameworkCore")).ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_DoesNotReferenceMultitenancyCore()
    {
        // Abstractions is the base layer — Core depends on it, never the reverse.
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Multitenancy").ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_ContainsNoConcreteContextAccessorImplementation()
    {
        // AsyncLocal-backed ITenantContextAccessor must live in Core, not in Abstractions.
        // Abstractions defines the contract; the implementation is in MicroKit.Multitenancy (Core).
        var concreteAccessors = AbstractionsAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantContextAccessor).IsAssignableFrom(t));

        concreteAccessors.ShouldBeEmpty(
            "AsyncLocal implementation must not leak into Abstractions — ITenantContextAccessor concrete impl belongs in Core");
    }

    [Fact]
    public void Abstractions_ContainsNoConcreteResolverImplementation()
    {
        // Resolution pipeline (TenantResolutionPipeline) must live in Core, not in Abstractions.
        var concreteResolvers = AbstractionsAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantResolver).IsAssignableFrom(t));

        concreteResolvers.ShouldBeEmpty(
            "ITenantResolver concrete impl must not live in Abstractions — it belongs in Core");
    }

    [Fact]
    public void Abstractions_ContainsNoConcreteStoreImplementation()
    {
        // InMemoryTenantStore and ConfigurationTenantStore belong in Core.
        var concreteStores = AbstractionsAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantStore).IsAssignableFrom(t));

        concreteStores.ShouldBeEmpty(
            "ITenantStore concrete impls must not live in Abstractions — they belong in Core or EntityFrameworkCore");
    }
}
