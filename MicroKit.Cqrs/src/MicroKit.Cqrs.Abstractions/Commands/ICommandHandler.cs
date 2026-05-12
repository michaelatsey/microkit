namespace MicroKit.Cqrs.Abstractions.Commands;

/// <summary>Handles a fire-and-forget command of type <typeparamref name="TCommand"/>.</summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>Handles a command of type <typeparamref name="TCommand"/> and returns a response.</summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
/// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>Executes the command and returns its result.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command result.</returns>
    Task<TResponse> HandleAsync(TCommand command, CancellationToken ct = default);
}
