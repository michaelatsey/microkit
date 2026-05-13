using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Data.SqlServer;

namespace MicroKit.Resilience.Extensions;

/// <summary>
/// Extension methods for adding SQL Server-specific resilience detection to the builder.
/// </summary>
public static class SqlServerResilienceExtensions
{
    /// <summary>
    /// Registers the SQL Server resilience detector to handle transient SQL Server exceptions.
    /// </summary>
    /// <param name="builder">The resilience builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static MicroKitResilienceBuilder AddSqlServer(this MicroKitResilienceBuilder builder)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        builder.AddDetector<SqlResilienceDetector>();
        return builder;
    }
}
