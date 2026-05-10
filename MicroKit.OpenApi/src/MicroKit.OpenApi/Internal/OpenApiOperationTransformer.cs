using MicroKit.OpenApi.Filters;
using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Internal;

/// <summary>
/// Transforms OpenAPI operations with MicroKit filters.
/// </summary>
internal sealed class OpenApiOperationTransformer : IOpenApiOperationTransformer
{
    private readonly FilterRegistry _filterRegistry;
    private readonly IServiceProvider _serviceProvider;

    public OpenApiOperationTransformer(
        FilterRegistry filterRegistry,
        IServiceProvider serviceProvider)
    {
        _filterRegistry = filterRegistry;
        _serviceProvider = serviceProvider;
    }

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var filterContext = new OperationFilterContext
        {
            ApiDescription = context.Description,
            DocumentName = context.DocumentName,
            ServiceProvider = _serviceProvider
        };

        foreach (var filterType in _filterRegistry.OperationFilters)
        {
            var filter = (IOpenApiOperationFilter)_serviceProvider.GetRequiredService(filterType);
            await filter.ApplyAsync(operation, filterContext, cancellationToken);
        }
    }
}
