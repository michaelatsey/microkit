using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using ICommand = MicroKit.Cqrs.Abstractions.Commands.ICommand;

namespace MicroKit.Cqrs.MediatR.Commands;

public sealed class MediatRCommandBus : ICommandBus
{
    private readonly ISender _mediator;

    public MediatRCommandBus(ISender mediator) => _mediator = mediator;

    public async Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
    {
        // On redirige vers MediatR
        await _mediator.Send(command, ct);
    }

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result is TResponse response)
        {
            return response;
        }

        throw new InvalidCastException($"Le résultat de MediatR ({result?.GetType().Name}) ne peut pas être converti en {typeof(TResponse).Name}");
    }
}
