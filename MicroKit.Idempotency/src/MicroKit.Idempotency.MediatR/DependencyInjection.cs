using MediatR;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.MediatR.Behaviors;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.MediatR;

/// <summary>Extension methods for registering the MediatR idempotency pipeline behavior via Microsoft DI.</summary>
public static class DependencyInjection
{
    /// <summary>Registers the <see cref="IdempotencyBehavior{TRequest,TResponse}"/> as a transient MediatR pipeline behavior.</summary>
    /// <param name="builder">The idempotency builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitIdempotencyBuilder UseMediatRPipeline(this MicroKitIdempotencyBuilder builder)
    {

        builder.Services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        return builder;
    }
}
