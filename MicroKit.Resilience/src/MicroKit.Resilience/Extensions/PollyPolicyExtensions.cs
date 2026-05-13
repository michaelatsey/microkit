using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;

namespace MicroKit.Resilience.Extensions;

/// <summary>
/// Extension methods for configuring Polly resilience pipelines with the MicroKit builder.
/// </summary>
public static class PollyPolicyExtensions
{
    /// <summary>
    /// Registers a default retry policy with fallback and circuit breaker protection into the Polly registry.
    /// </summary>
    /// <param name="builder">The resilience builder.</param>
    /// <param name="configureOptions">Optional delegate to customize resilience options.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static MicroKitResilienceBuilder AddDefaultRetryPolicy(
        this MicroKitResilienceBuilder builder,
        Action<ResilienceRetryOptions>? configureOptions = null)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        // Initialize default options and apply customization if provided
        var options = new ResilienceRetryOptions();
        configureOptions?.Invoke(options);

        // Register options for injection into behaviors
        builder.Services.Configure<ResilienceRetryOptions>(opt =>
        {
            opt.RetryCount = options.RetryCount;
            opt.PipelineName = options.PipelineName;
            opt.BaseDelaySeconds = options.BaseDelaySeconds;
        });

        // Register Polly resilience pipeline in the centralized registry
        builder.Services.AddResiliencePipeline<string, object>(options.PipelineName, (pipelineBuilder, context) =>
        {
            var detector = context.ServiceProvider.GetRequiredService<IResilienceErrorDetector>();

            // Fallback strategy: provide default value on transient errors
            if (options.EnableFallback)
            {
                var predicateBuilder = new PredicateBuilder<object>().Handle<Exception>(ex => detector.ShouldRetry(ex));
                pipelineBuilder.AddFallback(new FallbackStrategyOptions<object>
                {
                    ShouldHandle = predicateBuilder,
                    OnFallback = _ => default,
                    FallbackAction = args => new ValueTask<Outcome<object>>(
                        Outcome.FromException<object>(
                            new OperationCanceledException(
                                "The service did not respond successfully after multiple retry attempts.",
                                args.Outcome.Exception)))
                });
            }

            // Circuit breaker: stop making requests if the service is degraded
            if (options.EnableCircuitBreaker)
            {
                pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<object>
                {
                    ShouldHandle = new PredicateBuilder<object>().Handle<Exception>(ex => detector.ShouldRetry(ex)),
                    FailureRatio = options.FailureRatio,
                    MinimumThroughput = options.MinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = options.BreakDuration
                });
            }

            // Retry with exponential backoff
            pipelineBuilder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => detector.ShouldRetry(ex)),
                MaxRetryAttempts = options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true, // Recommended in microservices to avoid thundering herd
                Delay = TimeSpan.FromSeconds(options.BaseDelaySeconds),
                OnRetry = args =>
                {
                    // Log retry attempts for observability
                    var logger = context.ServiceProvider
                        .GetService<ILoggerFactory>()?
                        .CreateLogger("MicroKit.Resilience");

                    logger?.LogWarning(
                        args.Outcome.Exception,
                        "[{Pipeline}] Retry {Attempt}: Waiting {Duration}ms due to {Error}",
                        options.PipelineName,
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);

                    return default;
                }
            });
        });

        return builder;
    }
}
