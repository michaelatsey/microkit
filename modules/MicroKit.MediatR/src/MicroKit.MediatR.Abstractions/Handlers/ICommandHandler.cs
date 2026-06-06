using MicroKit.MediatR.Requests;

namespace MicroKit.MediatR.Handlers;

/// <summary>
/// Handles <typeparamref name="TCommand"/> commands that return no value.
/// </summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>Handles the specified command.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask Handle(TCommand command, CancellationToken ct = default);
}

/// <summary>
/// Handles <typeparamref name="TCommand"/> commands that return <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
/// <typeparam name="TResult">The result type. Typically <c>Result&lt;TId&gt;</c> or <c>Result&lt;Unit&gt;</c>.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Handles the specified command.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The result of executing the command.</returns>
    ValueTask<TResult> Handle(TCommand command, CancellationToken ct = default);
}
