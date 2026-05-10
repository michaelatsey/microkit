using MicroKit.Resilience.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Resilience;

public static class ServiceCollectionExtensions
{
    public static MicroKitResilienceBuilder AddMicroKitResilience(this IServiceCollection services)
    {
        return services == null 
            ? throw new ArgumentNullException(nameof(services)) 
            : new MicroKitResilienceBuilder(services);
    }
}
