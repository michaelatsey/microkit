using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Interface for OpenAPI operation filters.
/// </summary>
public interface IOpenApiOperationFilter
{
    /// <summary>
    /// Applies the filter to the OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation.</param>
    /// <param name="context">The filter context.</param>
    /// <param name="cancellationToken"></param>
    Task ApplyAsync(OpenApiOperation operation, OperationFilterContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for operation filters.
/// </summary>
public sealed class OperationFilterContext
{
    /// <summary>
    /// Gets the API description.
    /// </summary>
    public required ApiDescription ApiDescription { get; init; }

    /// <summary>
    /// Gets the document name (version).
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>
    /// Gets the service provider.
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }
}
