namespace MicroKit.MediatR;

internal sealed class CommandHandlerAdapter<TCommand>(ICommandHandler<TCommand> inner)
    : IRequestHandler<TCommand>
    where TCommand : ICommand
{
    public Task Handle(TCommand request, CancellationToken cancellationToken)
    {
        var vt = inner.Handle(request, cancellationToken);
        // Avoid state-machine allocation on the synchronous fast path (ADR-003).
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }
}

internal sealed class CommandHandlerAdapter<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner)
    : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public Task<TResult> Handle(TCommand request, CancellationToken cancellationToken)
    {
        var vt = inner.Handle(request, cancellationToken);
        // Avoid Task wrapper allocation on the synchronous fast path (ADR-003).
        return vt.IsCompletedSuccessfully ? Task.FromResult(vt.Result) : vt.AsTask();
    }
}
