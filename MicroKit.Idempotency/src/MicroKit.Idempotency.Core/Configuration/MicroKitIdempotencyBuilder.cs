using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Idempotency.Core.Configuration;

public class MicroKitIdempotencyBuilder
{
    public IServiceCollection Services { get; }

    public MicroKitIdempotencyBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public MicroKitIdempotencyBuilder Configure(Action<IdempotencyOptions>? configure = null)
    {
        if(configure is not null)
        {
            Services.Configure(configure);
        }
        return this;
    }

    
}
