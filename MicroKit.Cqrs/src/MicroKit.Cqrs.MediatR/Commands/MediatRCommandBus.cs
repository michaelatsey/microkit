using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;

namespace MicroKit.Cqrs.MediatR.Commands;

public sealed class MediatRCommandBus : ICommandBus
{
    private readonly ISender _mediator;

    public MediatRCommandBus(ISender mediator) => _mediator = mediator;

    /// <inheritdoc/>
    public async Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
    {
        if (command is IRequest<Unit> unitRequest)
        {
            await _mediator.Send(unitRequest, ct);
            return;
        }

        // Fallback: dynamic dispatch for commands not using the Unit return convention.
        await _mediator.Send((object)command!, ct);
    }

    /// <inheritdoc/>
    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        if (command is IRequest<TResponse> typedRequest)
            return await _mediator.Send(typedRequest, cancellationToken);

        var result = await _mediator.Send((object)command!, cancellationToken);
        return result is TResponse response
            ? response
            : throw new InvalidOperationException(
                $"MediatR returned '{result?.GetType().Name ?? "null"}' but expected '{typeof(TResponse).Name}'.");
    }
}
