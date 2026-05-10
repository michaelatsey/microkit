using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Interface for OpenAPI schema filters.
/// </summary>
public interface IOpenApiSchemaFilter
{
    /// <summary>
    /// Applies the filter to the OpenAPI schema.
    /// </summary>
    /// <param name="schema">The OpenAPI schema.</param>
    /// <param name="context">The filter context.</param>
    /// <param name="cancellationToken"></param>
    Task ApplyAsync(OpenApiSchema schema, SchemaFilterContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for schema filters.
/// </summary>
public sealed class SchemaFilterContext
{
    /// <summary>
    /// Gets the type being documented.
    /// </summary>
    public required Type Type { get; init; }

    /// <summary>
    /// Gets the document name (version).
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>
    /// Gets the service provider.
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }
}
