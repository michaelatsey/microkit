using MicroKit.Messaging.Execution;

namespace MicroKit.Messaging.UnitTests.Execution;

/// <summary>
/// The default scope's provider serves keyed lookups (#188): every store service is resolved from a
/// message scope by the store's key, and a wrapper implementing <see cref="IServiceProvider"/> alone
/// made each of them throw "doesn't support keyed services".
/// </summary>
public sealed class PassThroughExecutionScopeTests
{
    private static readonly MessageStoreKey Store = new("tests:scope");

    [Fact]
    public async Task TheScopesProvider_ResolvesKeyedServicesFromTheScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ExecutionContextHolder>();
        services.AddKeyedScoped<KeyedProbe>(Store);
        var provider = services.BuildServiceProvider();
        var factory = new PassThroughExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

        var scope = await factory.CreateScopeAsync(new MicroKit.Messaging.Execution.ExecutionContext());
        await using (scope)
        {
            var first = scope.ServiceProvider.GetRequiredKeyedService<KeyedProbe>(Store);
            scope.ServiceProvider.GetKeyedService<KeyedProbe>(Store).ShouldBeSameAs(first, "one instance per scope");
            scope.ServiceProvider.GetKeyedService<KeyedProbe>(new MessageStoreKey("tests:other")).ShouldBeNull();
        }
    }

    private sealed class KeyedProbe;
}
