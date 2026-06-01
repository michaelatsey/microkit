namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="IQueryHandler{TQuery,TResult}"/> implementations.
/// Executes the handler in isolation — no DI container, no real <c>IMediator</c>.
/// </summary>
/// <typeparam name="TQuery">The query type, must implement <see cref="IQuery{TResult}"/>.</typeparam>
/// <typeparam name="TResult">The result type returned by the query handler.</typeparam>
/// <example>
/// <code>
/// private readonly IUserReadRepository _repo = Substitute.For&lt;IUserReadRepository&gt;();
/// private readonly QueryHandlerTestHarness&lt;GetUserByIdQuery, Result&lt;UserDto&gt;&gt; _harness;
///
/// public GetUserByIdHandlerTests()
///     => _harness = new(new GetUserByIdHandler(_repo));
///
/// [Fact]
/// public async Task Handle_WhenUserExists_ReturnsDto()
/// {
///     _repo.FindAsync(userId, Arg.Any&lt;CancellationToken&gt;()).Returns(user);
///     var result = await _harness.QueryAsync(new GetUserByIdQuery(userId));
///     result.IsSuccess.ShouldBeTrue();
///     result.Value.Id.ShouldBe(userId);
/// }
/// </code>
/// </example>
public sealed class QueryHandlerTestHarness<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _handler;

    /// <summary>
    /// Initialises the harness with the handler under test.
    /// </summary>
    /// <param name="handler">The query handler instance to test.</param>
    public QueryHandlerTestHarness(IQueryHandler<TQuery, TResult> handler) =>
        _handler = handler;

    /// <summary>
    /// Executes <paramref name="query"/> through the handler and returns the result.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The result produced by the handler.</returns>
    public async Task<TResult> QueryAsync(TQuery query, CancellationToken ct = default)
        => await _handler.Handle(query, ct).ConfigureAwait(false);
}
