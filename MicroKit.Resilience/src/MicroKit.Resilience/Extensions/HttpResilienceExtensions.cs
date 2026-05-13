using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Http;

namespace MicroKit.Resilience.Extensions;

/// <summary>
/// Extension methods for adding HTTP-specific resilience detection to the builder.
/// </summary>
public static class HttpResilienceExtensions
{
    /// <summary>
    /// Registers the HTTP resilience detector to handle transient HTTP and network exceptions.
    /// </summary>
    /// <param name="builder">The resilience builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static MicroKitResilienceBuilder AddHttp(this MicroKitResilienceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddDetector<HttpResilienceDetector>();
        return builder;
    }
}
