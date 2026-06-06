using MicroKit.MediatR.Requests;

namespace MicroKit.MediatR.Handlers;

/// <summary>
/// Handles <typeparamref name="TQuery"/> stream queries, producing items as <see cref="IAsyncEnumerable{TResult}"/>.
/// Stream query handlers must not mutate state.
/// </summary>
/// <typeparam name="TQuery">The stream query type to handle.</typeparam>
/// <typeparam name="TResult">The item type produced by the stream.</typeparam>
/// <remarks>
/// Implementations must apply <c>[EnumeratorCancellation]</c> to the <c>CancellationToken</c> parameter
/// and use <c>await foreach ... ConfigureAwait(false)</c> when enumerating inner sequences.
/// </remarks>
public interface IStreamQueryHandler<in TQuery, TResult>
    where TQuery : IStreamQuery<TResult>
{
    /// <summary>Handles the specified stream query, yielding results as they become available.</summary>
    /// <param name="query">The stream query to handle.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>An async sequence of <typeparamref name="TResult"/> items.</returns>
    IAsyncEnumerable<TResult> Handle(TQuery query, CancellationToken ct = default);
}
