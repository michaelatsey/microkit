using MicroKit.MediatR.Requests;

namespace MicroKit.MediatR.Handlers;

/// <summary>
/// Handles <typeparamref name="TQuery"/> queries that return <typeparamref name="TResult"/>.
/// Query handlers must not mutate state.
/// </summary>
/// <typeparam name="TQuery">The query type to handle.</typeparam>
/// <typeparam name="TResult">The result type. Typically <c>Result&lt;TDto&gt;</c> for queries that may produce not-found.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Handles the specified query.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The query result.</returns>
    ValueTask<TResult> Handle(TQuery query, CancellationToken ct = default);
}
