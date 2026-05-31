namespace MicroKit.MediatR;

/// <summary>
/// Marks a request that reads state and returns <typeparamref name="TResult"/>. Must not mutate state.
/// Implement <see cref="IQueryHandler{TQuery,TResult}"/> to handle this query.
/// </summary>
/// <typeparam name="TResult">The return type. Typically <c>Result&lt;TDto&gt;</c> for queries that may return not-found.</typeparam>
public interface IQuery<TResult> : IRequest<TResult>;
