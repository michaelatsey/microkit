using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;

namespace MicroKit.Cqrs.MediatR.Handlers;

/// <summary>
/// Base class for query handlers that bridges <see cref="IQueryHandler{TQuery,TResponse}"/>
/// with MediatR's <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// Inherit from this class instead of implementing both interfaces separately.
/// </summary>
/// <typeparam name="TQuery">
/// The query type. Must implement both <see cref="IQuery{TResponse}"/> and
/// <see cref="IRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The type of value produced by the query.</typeparam>
public abstract class QueryHandler<TQuery, TResponse>
    : IQueryHandler<TQuery, TResponse>, IRequestHandler<TQuery, TResponse>
    where TQuery : class, IQuery<TResponse>, IRequest<TResponse>
{
    /// <inheritdoc/>
    public abstract Task<TResponse> HandleAsync(TQuery query, CancellationToken ct = default);

    /// <inheritdoc/>
    public Task<TResponse> Handle(TQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);
}
