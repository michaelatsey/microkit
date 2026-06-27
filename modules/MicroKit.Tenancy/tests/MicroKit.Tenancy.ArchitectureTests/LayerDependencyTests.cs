// GetReferencedAssemblies() checks direct references only (assembly manifest). Transitive refs are not checked.

namespace MicroKit.Tenancy.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(TenantId).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(AsyncLocalTenantContextAccessor).Assembly;

    private static readonly Assembly AspNetCoreAssembly =
        typeof(MicroKit.Tenancy.AspNetCore.TenantResolutionMiddleware).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Tenancy.EntityFrameworkCore.TenantStampInterceptor).Assembly;

    // --- Core: must stay host-agnostic and EF-free ---

    [Fact]
    public void Core_DoesNotReferenceAspNetCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.AspNetCore")).ShouldBeFalse();
    }

    [Fact]
    public void Core_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.EntityFrameworkCore")).ShouldBeFalse();
    }

    [Fact]
    public void Core_DoesNotReferenceMultitenancyAspNetCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy.AspNetCore").ShouldBeFalse();
    }

    [Fact]
    public void Core_DoesNotReferenceMultitenancyEntityFrameworkCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy.EntityFrameworkCore").ShouldBeFalse();
    }

    // --- Sibling isolation: AspNetCore ↔ EntityFrameworkCore ---

    [Fact]
    public void AspNetCore_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.EntityFrameworkCore")).ShouldBeFalse();
    }

    [Fact]
    public void AspNetCore_DoesNotReferenceMultitenancyEntityFrameworkCore()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy.EntityFrameworkCore").ShouldBeFalse();
    }

    [Fact]
    public void EntityFrameworkCore_DoesNotReferenceAspNetCore()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.AspNetCore")).ShouldBeFalse();
    }

    [Fact]
    public void EntityFrameworkCore_DoesNotReferenceMultitenancyAspNetCore()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy.AspNetCore").ShouldBeFalse();
    }

    // --- Correct dependency direction ---

    [Fact]
    public void AspNetCore_References_MultitenancyCore()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy").ShouldBeTrue();
    }

    [Fact]
    public void EntityFrameworkCore_References_MultitenancyCore()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy").ShouldBeTrue();
    }

    [Fact]
    public void Core_References_Abstractions()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Tenancy.Abstractions").ShouldBeTrue();
    }
}
