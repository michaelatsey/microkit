using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

public interface ICommand: IRequest
{
}
// Version avec retour (ex: Guid de l'entité créée)
public interface ICommand<out TResponse> : IRequest<TResponse> { }