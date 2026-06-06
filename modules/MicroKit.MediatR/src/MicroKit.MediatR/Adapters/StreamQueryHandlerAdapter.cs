using System.Runtime.CompilerServices;

namespace MicroKit.MediatR.Adapters;

internal sealed class StreamQueryHandlerAdapter<TQuery, TResult>(IStreamQueryHandler<TQuery, TResult> inner)
    : IStreamRequestHandler<TQuery, TResult>
    where TQuery : IStreamQuery<TResult>
{
    public async IAsyncEnumerable<TResult> Handle(
        TQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in inner.Handle(request, cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}
