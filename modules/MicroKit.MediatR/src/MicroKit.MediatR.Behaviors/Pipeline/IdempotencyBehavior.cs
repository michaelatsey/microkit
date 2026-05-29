using MicroKit.MediatR.Behaviors.Idempotency;

namespace MicroKit.MediatR.Behaviors.Pipeline;

/// <summary>
/// Deduplicates <see cref="IIdempotentCommand"/> commands via <see cref="IIdempotencyStore"/>.
/// On a cache hit, returns the stored response without executing the handler.
/// Never stores a <c>Result.Failure</c> response — only successful outcomes are idempotent.
/// Commands only (queries and stream queries pass through).
/// Pipeline order: <see cref="PipelineOrder.Idempotency"/> (400).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class IdempotencyBehavior<TRequest, TResponse>(IIdempotencyStore store)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public override int Order => PipelineOrder.Idempotency;

    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand idempotentCommand)
            return await next().ConfigureAwait(false);

        var key = idempotentCommand.IdempotencyKey;
        if (string.IsNullOrEmpty(key))
            // Null/empty IdempotencyKey is a programming error (IIdempotentCommand contract violation),
            // not a business-rule failure. Always throws regardless of TResponse — see pipeline-behaviors.md.
            // CreateFailureOrThrow is intentionally NOT used here.
            throw new InvalidOperationException(
                $"'{typeof(TRequest).Name}' implements IIdempotentCommand but returned a null or empty IdempotencyKey. " +
                $"IdempotencyKey must be a non-null, non-empty, deterministic value derived from the command's inputs.");

        var cached = await store.GetAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
        // For reference-type TResponse, null indicates a cache miss.
        // For struct TResponse (e.g. Result<T>), TResponse? is TResponse at IL level in unconstrained
        // generics — structs can never be null — so use EqualityComparer to distinguish a real
        // zero-initialized default from a legitimately stored value.
        if (!EqualityComparer<TResponse?>.Default.Equals(cached, default(TResponse?)))
            return cached!;

        var response = await next().ConfigureAwait(false);

        // Never store a failure — a failed command is not an idempotent success to replay.
        if (!ResultInspector<TResponse>.IsFailure(response))
            await store.SetAsync(key, response, cancellationToken).ConfigureAwait(false);

        return response;
    }
}
