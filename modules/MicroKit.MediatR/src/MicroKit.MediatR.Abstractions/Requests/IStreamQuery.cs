namespace MicroKit.MediatR;

/// <summary>
/// Marks a request that reads state and streams <typeparamref name="TResult"/> items as <see cref="IAsyncEnumerable{T}"/>.
/// Use for large or unbounded datasets where buffering the full result set is impractical.
/// Implement <see cref="IStreamQueryHandler{TQuery,TResult}"/> to handle this query.
/// </summary>
/// <typeparam name="TResult">The item type produced by the stream.</typeparam>
public interface IStreamQuery<TResult> : IStreamRequest<TResult>;
