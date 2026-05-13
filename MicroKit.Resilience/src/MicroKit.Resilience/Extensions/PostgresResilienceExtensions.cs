using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Data.PostgreSQL;

namespace MicroKit.Resilience.Extensions;

/// <summary>
/// Extension methods for adding PostgreSQL-specific resilience detection to the builder.
/// </summary>
public static class PostgresResilienceExtensions
{
    /// <summary>
    /// Registers the PostgreSQL resilience detector to handle transient PostgreSQL exceptions.
    /// </summary>
    /// <param name="builder">The resilience builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static MicroKitResilienceBuilder AddPostgres(this MicroKitResilienceBuilder builder)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        builder.AddDetector<PostgresResilienceDetector>();
        return builder;
    }
}
