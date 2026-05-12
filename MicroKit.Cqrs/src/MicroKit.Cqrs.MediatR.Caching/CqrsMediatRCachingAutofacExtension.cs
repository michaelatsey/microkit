using MicroKit.Caching.Distributed.Autofac;
using MicroKit.Cqrs.MediatR.Autofac.Builder;
namespace MicroKit.Cqrs.MediatR.Caching;

/// <summary>Extension methods for registering the distributed cache MediatR caching pipeline.</summary>
public static class CqrsMediatRCachingExtension
{
    /// <summary>Registers the distributed cache services into the Autofac container via the CQRS MediatR builder.</summary>
    /// <param name="builder">The CQRS MediatR builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static CqrsMediatRBuilder UseDistributedCache(this CqrsMediatRBuilder builder)
    {
        builder.Builder.RegisterMicroKitDistributedCache();
        return builder;
    }

    //public static CqrsMediatRBuilder UseDistributedCachePipelines(
    //    this CqrsMediatRBuilder qrsMediatRBuilder,
    //    Action<CqrsMediatRCachingBuilder>? config = null
    //    )
    //{
    //    CqrsMediatRCachingBuilder innerBuilder = new (qrsMediatRBuilder.Builder);
    //    config?.Invoke(innerBuilder);

    //    innerBuilder.Build();

    //    return qrsMediatRBuilder;
    //}
}
