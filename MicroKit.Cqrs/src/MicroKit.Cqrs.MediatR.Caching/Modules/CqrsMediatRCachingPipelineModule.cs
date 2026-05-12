/*using Autofac;
using MediatR;
using MicroKit.Cqrs.MediatR.Caching.Pipelines;

namespace MicroKit.Cqrs.MediatR.Caching.Modules;

public class CqrsMediatRCachingPipelineModule : Module
{
    private readonly IEnumerable<Type> _middlePipelines;

    /// <summary>
    /// Initializes a new instance of the <see cref="CqrsMediatRCachingPipelineModule"/> class.
    /// </summary>
    /// <param name="customBehaviors">The custom behaviors.</param>
    public CqrsMediatRCachingPipelineModule(IEnumerable<Type>? customBehaviors = null)
    {
        _middlePipelines = customBehaviors ?? [];
    }
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterGeneric(typeof(CachingBehavior<,>))
            .As(typeof(IPipelineBehavior<,>))
            .InstancePerLifetimeScope();

        // 2. DYNAMIC REGISTRATION (Les autres modules)
        foreach (var pipeline in _middlePipelines)
        {
            builder.RegisterGeneric(pipeline)
                .As(typeof(IPipelineBehavior<,>)).InstancePerLifetimeScope();
        }

        builder.RegisterGeneric(typeof(CacheInvalidationBehavior<,>))
            .As(typeof(IPipelineBehavior<,>))
            .InstancePerLifetimeScope();

    }
}
*/