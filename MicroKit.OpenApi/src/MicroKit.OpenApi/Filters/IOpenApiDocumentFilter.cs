using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Interface for OpenAPI document filters.
/// </summary>
public interface IOpenApiDocumentFilter
{
    /// <summary>
    /// Applies the filter to the OpenAPI document.
    /// </summary>
    /// <param name="document">The OpenAPI document.</param>
    /// <param name="context">The filter context.</param>
    /// <param name="cancellationToken"></param>
    Task ApplyAsync(OpenApiDocument document, DocumentFilterContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for document filters.
/// </summary>
public sealed class DocumentFilterContext
{
    /// <summary>
    /// Gets the document name (version).
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>
    /// Gets the API version.
    /// </summary>
    public required string ApiVersion { get; init; }

    /// <summary>
    /// Gets whether the version is deprecated.
    /// </summary>
    public required bool IsDeprecated { get; init; }

    /// <summary>
    /// Gets the service provider.
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }
}
