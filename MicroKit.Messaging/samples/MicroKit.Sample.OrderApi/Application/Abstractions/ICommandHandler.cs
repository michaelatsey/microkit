using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

// Pour les commandes sans retour (Void/Task)
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand
{
}

// Pour les commandes avec retour
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}
