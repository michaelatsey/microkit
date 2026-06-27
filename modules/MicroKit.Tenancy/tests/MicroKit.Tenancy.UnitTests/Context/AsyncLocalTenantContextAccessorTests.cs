using MicroKit.Tenancy;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.UnitTests.Context;

public sealed class AsyncLocalTenantContextAccessorTests
{
    [Fact]
    public void SetTenant_GetCurrentTenant_ReturnsSameTenant()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var tenant = Substitute.For<ITenantInfo>();

        accessor.SetTenant(tenant);

        accessor.CurrentTenant.ShouldBe(tenant);
    }

    [Fact]
    public void SetTenant_Null_ClearsCurrentTenant()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        accessor.SetTenant(Substitute.For<ITenantInfo>());

        accessor.SetTenant(null);

        accessor.CurrentTenant.ShouldBeNull();
    }

    [Fact]
    public void CreateScope_WhileActive_SetsScopedTenant()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var tenant = Substitute.For<ITenantInfo>();

        using (accessor.CreateScope(tenant))
        {
            accessor.CurrentTenant.ShouldBe(tenant);
        }
    }

    [Fact]
    public void CreateScope_AfterDispose_RestoresPreviousTenant()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var previous = Substitute.For<ITenantInfo>();
        var scoped = Substitute.For<ITenantInfo>();

        accessor.SetTenant(previous);

        using (accessor.CreateScope(scoped))
        {
            accessor.CurrentTenant.ShouldBe(scoped);
        }

        accessor.CurrentTenant.ShouldBe(previous);
    }

    [Fact]
    public void CreateScope_NestedScopes_RestoreInLIFOOrder()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var outer = Substitute.For<ITenantInfo>();
        var middle = Substitute.For<ITenantInfo>();
        var inner = Substitute.For<ITenantInfo>();

        accessor.SetTenant(outer);

        using (var scope1 = accessor.CreateScope(middle))
        {
            accessor.CurrentTenant.ShouldBe(middle);

            using (var scope2 = accessor.CreateScope(inner))
            {
                accessor.CurrentTenant.ShouldBe(inner);
            }

            accessor.CurrentTenant.ShouldBe(middle);
        }

        accessor.CurrentTenant.ShouldBe(outer);
    }

    [Fact]
    public async Task AsyncLocal_ParallelTasks_EachSeesOwnTenant()
    {
        // CreateScope() is called INSIDE each Task.Run lambda — critical per architect note
        var accessor = new AsyncLocalTenantContextAccessor();
        var tenantA = Substitute.For<ITenantInfo>();
        var tenantB = Substitute.For<ITenantInfo>();

        ITenantInfo? seenInTaskA = null;
        ITenantInfo? seenInTaskB = null;

        var taskA = Task.Run(async () =>
        {
            using var _ = accessor.CreateScope(tenantA);
            await Task.Yield();
            seenInTaskA = accessor.CurrentTenant;
        });

        var taskB = Task.Run(async () =>
        {
            using var _ = accessor.CreateScope(tenantB);
            await Task.Yield();
            seenInTaskB = accessor.CurrentTenant;
        });

        await Task.WhenAll(taskA, taskB);

        seenInTaskA.ShouldBe(tenantA);
        seenInTaskB.ShouldBe(tenantB);
    }
}
