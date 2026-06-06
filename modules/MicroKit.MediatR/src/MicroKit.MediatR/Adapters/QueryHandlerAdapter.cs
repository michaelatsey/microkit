namespace MicroKit.MediatR.Adapters;

internal sealed class QueryHandlerAdapter<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner)
    : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public Task<TResult> Handle(TQuery request, CancellationToken cancellationToken)
    {
        var vt = inner.Handle(request, cancellationToken);
        // Avoid Task wrapper allocation on the synchronous fast path (ADR-003).
        return vt.IsCompletedSuccessfully ? Task.FromResult(vt.Result) : vt.AsTask();
    }
}
