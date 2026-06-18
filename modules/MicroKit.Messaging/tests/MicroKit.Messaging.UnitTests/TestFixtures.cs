using MicroKit.Messaging.Execution;

namespace MicroKit.Messaging.UnitTests;

/// <summary>
/// In-process <see cref="IExecutionScopeFactory"/> for unit tests. Wraps the test
/// <see cref="IServiceScopeFactory"/> with no tenant hydration.
/// </summary>
internal sealed class TestExecutionScopeFactory(IServiceScopeFactory scopeFactory) : IExecutionScopeFactory
{
    public ValueTask<IExecutionScope> CreateScopeAsync(
        IExecutionContext context, CancellationToken ct = default)
        => ValueTask.FromResult<IExecutionScope>(
            new TestExecutionScope(scopeFactory.CreateAsyncScope()));
}

internal sealed class TestExecutionScope(AsyncServiceScope scope) : IExecutionScope
{
    public IServiceProvider ServiceProvider => scope.ServiceProvider;
    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
