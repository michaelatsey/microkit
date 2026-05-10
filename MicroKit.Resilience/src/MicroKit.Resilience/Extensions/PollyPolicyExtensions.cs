using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;

namespace MicroKit.Resilience.Extensions;

public static class PollyPolicyExtensions
{
    public static MicroKitResilienceBuilder AddDefaultRetryPolicy(
        this MicroKitResilienceBuilder builder,
        Action<ResilienceRetryOptions>? configureOptions = null)
    {
        // On initialise les options par défaut
        var options = new ResilienceRetryOptions();
        configureOptions?.Invoke(options);

        // On enregistre les options pour qu'elles soient injectables dans le Behavior
        builder.Services.Configure<ResilienceRetryOptions>(opt => {
            opt.RetryCount = options.RetryCount;
            opt.PipelineName = options.PipelineName;
            opt.BaseDelaySeconds = options.BaseDelaySeconds;
        });
        
        // On enregistre la Policy dans le Registry de Polly
        builder.Services.AddResiliencePipeline<string, object>(options.PipelineName, (pipelineBuilder, context) =>
        {
            // On récupère le détecteur d'erreurs depuis le conteneur de services
            var detector = context.ServiceProvider.GetRequiredService<IResilienceErrorDetector>();

            if (options.EnableFallback)
            {
                var predicateBuilder = new PredicateBuilder<object>().Handle<Exception>(ex => detector.ShouldRetry(ex));
                // On utilise FallbackStrategyOptions<object> car le pipeline est multi-types
                // Ou on définit des pipelines spécifiques par type de retour.
                pipelineBuilder.AddFallback(new FallbackStrategyOptions<object>
                {
                    ShouldHandle = predicateBuilder,
                    OnFallback = _ => default,
                    FallbackAction = args 
                        => throw new OperationCanceledException("Le service n'a pas pu répondre après plusieurs tentatives.", args.Outcome.Exception)
                });
            }

            // 2. CIRCUIT BREAKER : On coupe si le service est "mourant"
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

            pipelineBuilder.AddRetry(new RetryStrategyOptions
            {   
                // C'est ici que le package injecte sa logique dans Polly
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => detector.ShouldRetry(ex)),

                MaxRetryAttempts = options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true, // Recommandé en microservices pour éviter les pics de charge
                Delay = TimeSpan.FromSeconds(options.BaseDelaySeconds),
                OnRetry = args =>
                {
                    // On récupère le logger via le service provider présent dans le contexte d'exécution
                    var logger = context.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("MicroKit.Resilience");
                    // On logue l'erreur, le numéro de tentative et le délai avant le prochain essai
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
