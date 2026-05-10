using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;

namespace MicroKit.Cqrs.MediatR.Handlers;

/// <summary>
/// Base class for void command handlers that bridges <see cref="ICommandHandler{TCommand}"/>
/// with MediatR's <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// Inherit from this class instead of implementing both interfaces separately.
/// </summary>
/// <typeparam name="TCommand">
/// The command type. Must implement both <see cref="ICommand"/> and
/// <see cref="IRequest{TResponse}">IRequest&lt;Unit&gt;</see>.
/// </typeparam>
public abstract class CommandHandler<TCommand>
    : ICommandHandler<TCommand>, IRequestHandler<TCommand, Unit>
    where TCommand : class, ICommand, IRequest<Unit>
{
    /// <inheritdoc/>
    public abstract Task HandleAsync(TCommand command, CancellationToken ct = default);

    /// <inheritdoc/>
    public async Task<Unit> Handle(TCommand request, CancellationToken cancellationToken)
    {
        await HandleAsync(request, cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Base class for command handlers that return a value, bridging
/// <see cref="ICommandHandler{TCommand,TResponse}"/> with MediatR's
/// <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TCommand">
/// The command type. Must implement both <see cref="ICommand{TResponse}"/> and
/// <see cref="IRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The type of value produced by the command.</typeparam>
public abstract class CommandHandler<TCommand, TResponse>
    : ICommandHandler<TCommand, TResponse>, IRequestHandler<TCommand, TResponse>
    where TCommand : class, ICommand<TResponse>, IRequest<TResponse>
{
    /// <inheritdoc/>
    public abstract Task<TResponse> HandleAsync(TCommand command, CancellationToken ct = default);

    /// <inheritdoc/>
    public Task<TResponse> Handle(TCommand request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);
}
