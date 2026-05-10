using MicroKit.OpenApi.Filters;
using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Internal;

/// <summary>
/// Transforms OpenAPI schemas with MicroKit filters.
/// </summary>
internal sealed class OpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    private readonly FilterRegistry _filterRegistry;
    private readonly IServiceProvider _serviceProvider;

    public OpenApiSchemaTransformer(
        FilterRegistry filterRegistry,
        IServiceProvider serviceProvider)
    {
        _filterRegistry = filterRegistry;
        _serviceProvider = serviceProvider;
    }

    public async Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var filterContext = new SchemaFilterContext
        {
            Type = context.JsonTypeInfo.Type,
            DocumentName = context.DocumentName,
            ServiceProvider = _serviceProvider
        };

        foreach (var filterType in _filterRegistry.SchemaFilters)
        {
            var filter = (IOpenApiSchemaFilter)_serviceProvider.GetRequiredService(filterType);
            await filter.ApplyAsync(schema, filterContext, cancellationToken);
        }
    }
}
