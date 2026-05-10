using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Http;

namespace MicroKit.Resilience.Extensions;

public static class HttpResilienceExtensions
{
    public static MicroKitResilienceBuilder AddHttp(this MicroKitResilienceBuilder builder)
    {
        // On ajoute le détecteur spécifique au builder
        builder.AddDetector<HttpResilienceDetector>();
        return builder;
    }
}
