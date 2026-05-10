using MicroKit.Resilience.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Resilience.Builder;

public class MicroKitResilienceBuilder
{
    public IServiceCollection Services { get; }

    public MicroKitResilienceBuilder(IServiceCollection services)
    {
        Services = services;
        // Par défaut, on enregistre le composite
        Services.AddSingleton<IResilienceErrorDetector, CompositeResilienceDetector>();
    }
    public MicroKitResilienceBuilder AddDetector<TDetector>()
        where TDetector : class, IResilienceStrategyDetector
    {
        Services.AddSingleton<IResilienceStrategyDetector, TDetector>();
        return this;
    }


}
