using Autofac;
using MediatR;
using MicroKit.Idempotency.MediatR.Behaviors;

namespace MicroKit.Idempotency.MediatR;

/// <summary>Autofac extension methods for registering the MicroKit idempotency MediatR pipeline behavior.</summary>
public static class AutofacDependencyInjection
{
    /// <summary>Registers the <see cref="IdempotencyBehavior{TRequest,TResponse}"/> as a MediatR pipeline behavior.</summary>
    /// <param name="builder">The Autofac container builder.</param>
    public static void RegisterMicroKitIdempotencyPipelineBehavior(this ContainerBuilder builder)
    {
        builder.RegisterGeneric(typeof(IdempotencyBehavior<,>))
             .As(typeof(IPipelineBehavior<,>))
             .InstancePerLifetimeScope();
    }
}
