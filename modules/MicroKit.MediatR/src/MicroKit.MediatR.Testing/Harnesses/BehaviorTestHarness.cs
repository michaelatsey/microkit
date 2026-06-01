namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="IPipelineBehavior{TRequest,TResponse}"/> implementations
/// (typically subclasses of <see cref="BehaviorBase{TRequest,TResponse}"/>).
/// Executes the behavior in isolation with full control over the <c>next</c> delegate.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <example>
/// <code>
/// // Test pass-through when the opt-in marker is absent (zero cost guard):
/// var behavior = new CachingBehavior&lt;PlainQuery, Result&lt;string&gt;&gt;(cache, logger);
/// var harness = new BehaviorTestHarness&lt;PlainQuery, Result&lt;string&gt;&gt;(behavior);
///
/// await harness.ExecuteAsync(new PlainQuery(), Result.Success("data"));
/// harness.NextWasCalled.ShouldBeTrue(); // no ICacheableQuery marker → passed through
///
/// // Test short-circuit when marker is present and cache has a hit:
/// var cachedQuery = new CachedQuery(); // implements ICacheableQuery
/// var cachedHarness = new BehaviorTestHarness&lt;CachedQuery, Result&lt;string&gt;&gt;(behavior);
/// cache.GetAsync(cachedQuery.CacheKey).Returns(Encoding.UTF8.GetBytes("\"cached\""));
///
/// var result = await cachedHarness.ExecuteAsync(cachedQuery, Result.Success("fresh"));
/// cachedHarness.NextWasCalled.ShouldBeFalse(); // cache hit → handler not called
/// result.Value.ShouldBe("cached");
/// </code>
/// </example>
public sealed class BehaviorTestHarness<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPipelineBehavior<TRequest, TResponse> _behavior;
    private int _nextCallCount;

    /// <summary>
    /// Initialises the harness with the behavior under test.
    /// </summary>
    /// <param name="behavior">
    /// The pipeline behavior to test. Typically a subclass of
    /// <see cref="BehaviorBase{TRequest,TResponse}"/>.
    /// </param>
    public BehaviorTestHarness(IPipelineBehavior<TRequest, TResponse> behavior) =>
        _behavior = behavior;

    /// <summary>Gets the number of times the <c>next</c> delegate was invoked in the last <c>ExecuteAsync</c> call.</summary>
    public int NextCallCount => _nextCallCount;

    /// <summary>Gets a value indicating whether the <c>next</c> delegate was invoked at least once in the last <c>ExecuteAsync</c> call.</summary>
    public bool NextWasCalled => _nextCallCount > 0;

    /// <summary>
    /// Executes the behavior with a fixed <paramref name="nextResult"/> as the <c>next</c> delegate's return value.
    /// Resets <see cref="NextCallCount"/> before each call.
    /// </summary>
    /// <param name="request">The request to pass through the behavior.</param>
    /// <param name="nextResult">The value that the <c>next</c> delegate returns when invoked.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The response produced by the behavior (or by <c>next</c> on pass-through).</returns>
    public async Task<TResponse> ExecuteAsync(TRequest request, TResponse nextResult, CancellationToken ct = default)
    {
        _nextCallCount = 0;
        return await _behavior.Handle(request, () =>
        {
            _nextCallCount++;
            return Task.FromResult(nextResult);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the behavior with a custom <paramref name="next"/> delegate.
    /// Resets <see cref="NextCallCount"/> before each call.
    /// </summary>
    /// <param name="request">The request to pass through the behavior.</param>
    /// <param name="next">A delegate that is called if the behavior chooses to continue the pipeline.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The response produced by the behavior or by <paramref name="next"/>.</returns>
    public async Task<TResponse> ExecuteAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        _nextCallCount = 0;
        return await _behavior.Handle(request, () =>
        {
            _nextCallCount++;
            return next();
        }, ct).ConfigureAwait(false);
    }
}
