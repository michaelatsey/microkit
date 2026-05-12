using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.Abstractions.Commands;

/// <summary>Dispatches commands to their registered handlers.</summary>
public interface ICommandBus
{
    /// <summary>Dispatches a fire-and-forget command with no response.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand;

    /// <summary>Dispatches a command and returns its response.</summary>
    /// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The handler's response.</returns>
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
}
