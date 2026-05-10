using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;

namespace MicroKit.Cqrs.MediatR.Queries;

public sealed class MediatRQueryBus : IQueryBus
{
    private readonly ISender _mediator;

    public MediatRQueryBus(ISender mediator) => _mediator = mediator;

    public async Task<TResponse> AskAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send((IRequest<TResponse>)query, cancellationToken);
    }
}
