using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;

namespace MicroKit.Cqrs.MediatR.Queries;

public sealed class MediatRQueryBus : IQueryBus
{
    private readonly ISender _mediator;

    public MediatRQueryBus(ISender mediator) => _mediator = mediator;

    /// <inheritdoc/>
    public Task<TResponse> AskAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        if (query is IRequest<TResponse> typedRequest)
            return _mediator.Send(typedRequest, cancellationToken);

        throw new InvalidOperationException(
            $"Query '{query.GetType().Name}' must implement IRequest<{typeof(TResponse).Name}> to be dispatched via MediatR. " +
            "Inherit from QueryHandler<TQuery, TResponse> in your handler.");
    }
}
