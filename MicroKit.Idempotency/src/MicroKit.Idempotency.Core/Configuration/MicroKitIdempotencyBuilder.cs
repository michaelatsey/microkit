using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Idempotency.Core.Configuration;

/// <summary>Builder for configuring MicroKit Idempotency services.</summary>
public class MicroKitIdempotencyBuilder
{
    /// <summary>Gets the underlying service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="services">The service collection to configure.</param>
    public MicroKitIdempotencyBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>Applies additional <see cref="IdempotencyOptions"/> configuration.</summary>
    /// <param name="configure">Optional configuration delegate.</param>
    public MicroKitIdempotencyBuilder Configure(Action<IdempotencyOptions>? configure = null)
    {
        if(configure is not null)
        {
            Services.Configure(configure);
        }
        return this;
    }

    
}
