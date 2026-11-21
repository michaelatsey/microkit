namespace MicroKit.Messaging.ArchitectureTests;

/// <summary>
/// The store SPI's shape where a default would hide a regression (#188).
/// </summary>
public sealed class StoreContractTests
{
    /// <summary>
    /// One claim, taking the endpoint filter, with no default implementation: a default that ignored
    /// the filter would let a store provider bring back local-row starvation during a broker outage
    /// without a compile error.
    /// </summary>
    [Fact]
    public void IOutboxProcessorStore_HasOneClaim_TakingTheEndpointFilter_WithNoDefaultImplementation()
    {
        var claims = typeof(IOutboxProcessorStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(IOutboxProcessorStore.ClaimBatchAsync))
            .ToList();

        var claim = claims.ShouldHaveSingleItem("one claim signature");
        claim.GetParameters().Select(p => p.ParameterType)
            .ShouldBe([typeof(int), typeof(TimeSpan), typeof(OutboxEndpoints), typeof(CancellationToken)]);
        claim.IsAbstract.ShouldBeTrue("no default implementation: every store provider implements the filter");
    }
}
