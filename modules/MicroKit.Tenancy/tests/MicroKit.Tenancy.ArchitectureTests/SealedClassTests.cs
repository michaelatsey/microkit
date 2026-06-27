namespace MicroKit.Tenancy.ArchitectureTests;

public sealed class SealedClassTests
{
    // IsSealed on open generic type definitions always returns false in the CLR via Type.IsSealed.
    // TypeAttributes.Sealed is the correct way to check sealed for both open generic and non-generic types.
    private static bool IsSealed(Type type) =>
        (type.Attributes & TypeAttributes.Sealed) != 0;

    // --- Core: AsyncLocal accessor, resolution pipeline, stores ---

    [Fact]
    public void AsyncLocalTenantContextAccessor_IsSealed()
    {
        IsSealed(typeof(AsyncLocalTenantContextAccessor)).ShouldBeTrue();
    }

    [Fact]
    public void TenantResolutionPipeline_IsSealed()
    {
        IsSealed(typeof(TenantResolutionPipeline)).ShouldBeTrue();
    }

    [Fact]
    public void InMemoryTenantStore_IsSealed()
    {
        IsSealed(typeof(InMemoryTenantStore)).ShouldBeTrue();
    }

    [Fact]
    public void ConfigurationTenantStore_IsSealed()
    {
        IsSealed(typeof(ConfigurationTenantStore)).ShouldBeTrue();
    }

    // --- AspNetCore: middleware and resolution strategies ---

    [Fact]
    public void TenantResolutionMiddleware_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.AspNetCore.TenantResolutionMiddleware)).ShouldBeTrue();
    }

    [Fact]
    public void HeaderTenantResolutionStrategy_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.AspNetCore.HeaderTenantResolutionStrategy)).ShouldBeTrue();
    }

    [Fact]
    public void RouteDataTenantResolutionStrategy_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.AspNetCore.RouteDataTenantResolutionStrategy)).ShouldBeTrue();
    }

    [Fact]
    public void SubdomainTenantResolutionStrategy_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.AspNetCore.SubdomainTenantResolutionStrategy)).ShouldBeTrue();
    }

    [Fact]
    public void ClaimsTenantResolutionStrategy_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.AspNetCore.ClaimsTenantResolutionStrategy)).ShouldBeTrue();
    }

    [Fact]
    public void HostTenantResolutionStrategy_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.AspNetCore.HostTenantResolutionStrategy)).ShouldBeTrue();
    }

    // --- EntityFrameworkCore: interceptor, store, isolation scope ---

    [Fact]
    public void TenantStampInterceptor_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.EntityFrameworkCore.TenantStampInterceptor)).ShouldBeTrue();
    }

    [Fact]
    public void EfTenantStore_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.EntityFrameworkCore.EfTenantStore)).ShouldBeTrue();
    }

    [Fact]
    public void IgnoreTenantScope_IsSealed()
    {
        IsSealed(typeof(MicroKit.Tenancy.EntityFrameworkCore.IgnoreTenantScope)).ShouldBeTrue();
    }
}
