using Autofac;
using MediatR;
using MicroKit.Idempotency.MediatR.Behaviors;

namespace MicroKit.Idempotency.MediatR;

public static class AutofacDependencyInjection
{
    public static void  RegisterMicroKitIdempotencyPipelineBehavior(this ContainerBuilder builder)
    {
        builder.RegisterGeneric(typeof(IdempotencyBehavior<,>))
             .As(typeof(IPipelineBehavior<,>))
             .InstancePerLifetimeScope();
    }
}
