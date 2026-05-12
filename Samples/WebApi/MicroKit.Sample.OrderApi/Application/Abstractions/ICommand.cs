using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

/// <summary>Marker interface for commands that produce no return value.</summary>
public interface ICommand: IRequest
{
}

/// <summary>Marker interface for commands that produce a <typeparamref name="TResponse"/>.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse> { }