using MicroKit.OpenApi.Filters;

namespace MicroKit.OpenApi.Internal;

/// <summary>
/// Internal registry for OpenAPI filters.
/// </summary>
internal sealed class FilterRegistry
{
    private readonly List<Type> _documentFilters = [];
    private readonly List<Type> _operationFilters = [];
    private readonly List<Type> _schemaFilters = [];

    public IReadOnlyList<Type> DocumentFilters => _documentFilters.AsReadOnly();
    public IReadOnlyList<Type> OperationFilters => _operationFilters.AsReadOnly();
    public IReadOnlyList<Type> SchemaFilters => _schemaFilters.AsReadOnly();

    public void AddDocumentFilter<TFilter>() where TFilter : class, IOpenApiDocumentFilter
    {
        if (!_documentFilters.Contains(typeof(TFilter)))
        {
            _documentFilters.Add(typeof(TFilter));
        }
    }

    public void AddOperationFilter<TFilter>() where TFilter : class, IOpenApiOperationFilter
    {
        if (!_operationFilters.Contains(typeof(TFilter)))
        {
            _operationFilters.Add(typeof(TFilter));
        }
    }

    public void AddSchemaFilter<TFilter>() where TFilter : class, IOpenApiSchemaFilter
    {
        if (!_schemaFilters.Contains(typeof(TFilter)))
        {
            _schemaFilters.Add(typeof(TFilter));
        }
    }
}
