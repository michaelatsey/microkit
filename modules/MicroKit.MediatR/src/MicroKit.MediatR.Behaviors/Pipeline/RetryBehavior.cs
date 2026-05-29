namespace MicroKit.MediatR.Behaviors.Pipeline;

/// <summary>
/// Wraps the handler call in a Polly <see cref="ResiliencePipeline"/> with exponential
/// back-off retry. Retries on transient exceptions only; <c>Result.Failure</c> is never
/// retried (a business failure is not a transient error). Opt-in via <see cref="IRetryableRequest"/>.
/// Pipeline order: <see cref="PipelineOrder.Retry"/> (600).
/// </summary>
/// <remarks>
/// The Polly pipeline is cached by <typeparamref name="TRequest"/> type via a process-wide
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. <see cref="IRetryableRequest.MaxRetries"/>
/// and <see cref="IRetryableRequest.Delay"/> are read from the first dispatched instance and
/// treated as type-level constants — all subsequent instances of the same type share one pipeline.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RetryBehavior<TRequest, TResponse>
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    // Process-wide cache: one ResiliencePipeline per TRequest type.
    // Using a file-scoped static avoids per-(TRequest,TResponse)-pair duplication from CLR generic statics.
    private static readonly ConcurrentDictionary<Type, ResiliencePipeline> _pipelines = PipelineRegistry.Pipelines;

    /// <inheritdoc />
    public override int Order => PipelineOrder.Retry;

    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IRetryableRequest retryable)
            return await next().ConfigureAwait(false);

        var pipeline = _pipelines.GetOrAdd(typeof(TRequest), static (_, r) => BuildPipeline(r), retryable);

        return await pipeline.ExecuteAsync(
            async ct => await next().ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private static ResiliencePipeline BuildPipeline(IRetryableRequest retryable)
    {
        if (retryable.MaxRetries <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(retryable),
                retryable.MaxRetries,
                $"'{retryable.GetType().Name}.{nameof(IRetryableRequest.MaxRetries)}' must be greater than zero.");

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retryable.MaxRetries,
                Delay = retryable.Delay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(static ex =>
                        ex is not OperationCanceledException
                        and not ValidationException
                        and not UnauthorizedAccessException)
            })
            .Build();
    }
}

// Process-wide registry shared across all closed generics of RetryBehavior<TRequest,TResponse>.
// A single ConcurrentDictionary<Type, ResiliencePipeline> avoids per-(TRequest,TResponse) duplication.
file static class PipelineRegistry
{
    internal static readonly ConcurrentDictionary<Type, ResiliencePipeline> Pipelines = new();
}
