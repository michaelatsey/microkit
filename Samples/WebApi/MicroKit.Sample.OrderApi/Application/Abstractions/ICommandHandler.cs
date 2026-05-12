using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

/// <summary>MediatR handler interface for commands that produce no return value.</summary>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand
{
}

/// <summary>MediatR handler interface for commands that produce a <typeparamref name="TResponse"/>.</summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}
