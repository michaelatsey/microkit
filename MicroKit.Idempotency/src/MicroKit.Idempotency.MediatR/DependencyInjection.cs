using MediatR;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.MediatR.Behaviors;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.MediatR;

public static class DependencyInjection
{
    public static MicroKitIdempotencyBuilder UseMediatRPipeline(this MicroKitIdempotencyBuilder builder)
    {

        builder.Services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        return builder;
    }
}
