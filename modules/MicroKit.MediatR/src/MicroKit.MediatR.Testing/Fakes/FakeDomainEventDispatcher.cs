namespace MicroKit.MediatR.Testing;

/// <summary>
/// An in-memory implementation of <see cref="IDomainEventDispatcher"/> that records
/// each call to <see cref="DispatchEventsAsync"/>. Inject into pipeline behaviors under test
/// in place of the real dispatcher.
/// </summary>
/// <remarks>
/// Not thread-safe. Designed for single-threaded unit test execution.
/// </remarks>
/// <example>
/// <code>
/// var dispatcher = new FakeDomainEventDispatcher();
/// var behavior = new TransactionBehavior&lt;MyCommand, Result&gt;(
///     transactionalContext, dispatcher, unitOfWork);
///
/// await behavior.Handle(command, next, CancellationToken.None);
/// dispatcher.AssertDispatchCalledOnce();
/// </code>
/// </example>
public sealed class FakeDomainEventDispatcher : IDomainEventDispatcher
{
    private int _dispatchCallCount;

    /// <summary>Gets the number of times <see cref="DispatchEventsAsync"/> was called.</summary>
    public int DispatchCallCount => _dispatchCallCount;

    /// <inheritdoc/>
    public Task DispatchEventsAsync(CancellationToken ct = default)
    {
        _dispatchCallCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that <see cref="DispatchEventsAsync"/> was called at least once.
    /// Fails the test with a descriptive Shouldly message if it was never called.
    /// </summary>
    public void AssertDispatchWasCalled() =>
        _dispatchCallCount.ShouldBeGreaterThan(0,
            "Expected DispatchEventsAsync to be called, but it was never invoked.");

    /// <summary>
    /// Asserts that <see cref="DispatchEventsAsync"/> was called exactly once.
    /// </summary>
    public void AssertDispatchCalledOnce() =>
        _dispatchCallCount.ShouldBe(1,
            $"Expected DispatchEventsAsync to be called exactly once, but it was called {_dispatchCallCount} time(s).");

    /// <summary>
    /// Asserts that <see cref="DispatchEventsAsync"/> was never called.
    /// </summary>
    public void AssertDispatchNotCalled() =>
        _dispatchCallCount.ShouldBe(0,
            $"Expected DispatchEventsAsync not to be called, but it was called {_dispatchCallCount} time(s).");

    /// <summary>Clears call tracking. Use between test cases that share a harness instance.</summary>
    public void Reset() => _dispatchCallCount = 0;
}
