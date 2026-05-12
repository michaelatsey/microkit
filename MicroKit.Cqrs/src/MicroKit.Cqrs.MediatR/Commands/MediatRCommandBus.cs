using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;

namespace MicroKit.Cqrs.MediatR.Commands;

/// <summary>MediatR-backed implementation of <see cref="ICommandBus"/>.</summary>
public sealed class MediatRCommandBus : ICommandBus
{
    private readonly ISender _mediator;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="mediator">The MediatR sender used to dispatch commands.</param>
    public MediatRCommandBus(ISender mediator) => _mediator = mediator;

    /// <inheritdoc/>
    public Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
    {
        if (command is not IRequest<Unit> unitRequest)
            throw new InvalidOperationException(
                $"Command '{typeof(TCommand).Name}' must implement IRequest<Unit> to be dispatched via MediatR. " +
                "Inherit from CommandHandler<TCommand> in your handler.");

        return _mediator.Send(unitRequest, ct);
    }

    /// <inheritdoc/>
    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
    {
        if (command is not IRequest<TResponse> typedRequest)
            throw new InvalidOperationException(
                $"Command '{command.GetType().Name}' must implement IRequest<{typeof(TResponse).Name}> to be dispatched via MediatR. " +
                "Inherit from CommandHandler<TCommand, TResponse> in your handler.");

        return _mediator.Send(typedRequest, ct);
    }
}
