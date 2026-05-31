namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="IStreamQueryHandler{TQuery,TResult}"/> implementations.
/// Executes the handler in isolation — no DI container, no real <c>IMediator</c>.
/// </summary>
/// <typeparam name="TQuery">The stream query type, must implement <see cref="IStreamQuery{TResult}"/>.</typeparam>
/// <typeparam name="TResult">The item type produced by the stream.</typeparam>
/// <example>
/// <code>
/// private readonly IProductReadRepository _repo = Substitute.For&lt;IProductReadRepository&gt;();
/// private readonly StreamQueryHandlerTestHarness&lt;StreamProductsQuery, ProductDto&gt; _harness;
///
/// public StreamProductsHandlerTests()
///     => _harness = new(new StreamProductsHandler(_repo));
///
/// [Fact]
/// public async Task Handle_WhenProductsExist_YieldsAllItems()
/// {
///     var products = new[] { product1, product2 }.ToAsyncEnumerable();
///     _repo.StreamAsync(Arg.Any&lt;CancellationToken&gt;()).Returns(products);
///
///     var results = await _harness.StreamAsync(new StreamProductsQuery()).ToListAsync();
///     results.Count.ShouldBe(2);
/// }
/// </code>
/// </example>
public sealed class StreamQueryHandlerTestHarness<TQuery, TResult>
    where TQuery : IStreamQuery<TResult>
{
    private readonly IStreamQueryHandler<TQuery, TResult> _handler;

    /// <summary>
    /// Initialises the harness with the handler under test.
    /// </summary>
    /// <param name="handler">The stream query handler instance to test.</param>
    public StreamQueryHandlerTestHarness(IStreamQueryHandler<TQuery, TResult> handler) =>
        _handler = handler;

    /// <summary>
    /// Executes <paramref name="query"/> through the handler and returns the async stream.
    /// </summary>
    /// <param name="query">The stream query to execute.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>An async sequence of <typeparamref name="TResult"/> items.</returns>
    public IAsyncEnumerable<TResult> StreamAsync(TQuery query, CancellationToken ct = default)
        => _handler.Handle(query, ct);
}
