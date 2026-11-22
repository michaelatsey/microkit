namespace MicroKit.Messaging.ArchitectureTests;

/// <summary>
/// The store SPI's shape where a default would hide a regression (#188, #124).
/// </summary>
public sealed class StoreContractTests
{
    /// <summary>
    /// One claim, taking the endpoint filter and the tenant filter of each endpoint, with no default
    /// implementation: a default that ignored the endpoint filter would let a store provider bring back
    /// local-row starvation during a broker outage without a compile error, and one that ignored a tenant
    /// filter the re-deferral of a blocked tenant's whole backlog.
    /// </summary>
    [Fact]
    public void IOutboxProcessorStore_HasOneClaim_TakingTheEndpointAndTenantFilters_WithNoDefaultImplementation()
    {
        var claims = typeof(IOutboxProcessorStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(IOutboxProcessorStore.ClaimBatchAsync))
            .ToList();

        var claim = claims.ShouldHaveSingleItem("one claim signature");
        claim.GetParameters().Select(p => p.ParameterType).ShouldBe(
        [
            typeof(int), typeof(TimeSpan), typeof(OutboxEndpoints),
            typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>), typeof(CancellationToken),
        ]);
        claim.IsAbstract.ShouldBeTrue("no default implementation: every store provider implements the filters");
    }

    /// <summary>
    /// One claim, taking the tenant filter, with no default implementation: a default that ignored it
    /// would re-defer a blocked tenant's whole backlog each time its back-off ends (#124).
    /// </summary>
    [Fact]
    public void IInboxProcessorStore_HasOneClaim_TakingTheTenantFilter_WithNoDefaultImplementation()
    {
        var claims = typeof(IInboxProcessorStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(IInboxProcessorStore.ClaimBatchAsync))
            .ToList();

        var claim = claims.ShouldHaveSingleItem("one claim signature");
        claim.GetParameters().Select(p => p.ParameterType)
            .ShouldBe([typeof(int), typeof(TimeSpan), typeof(IReadOnlyList<string>), typeof(CancellationToken)]);
        claim.IsAbstract.ShouldBeTrue("no default implementation: every store provider implements the filter");
    }
}
