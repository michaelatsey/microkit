namespace MicroKit.Cqrs.Abstractions.Queries;

/// <summary>Handles a query of type <typeparamref name="TQuery"/> and returns a result of type <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TQuery">The query type to handle.</typeparam>
/// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>Executes the query and returns its result.</summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The query result.</returns>
    Task<TResponse> HandleAsync(TQuery query, CancellationToken ct = default);
}
