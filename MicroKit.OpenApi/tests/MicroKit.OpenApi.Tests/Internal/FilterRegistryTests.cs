using MicroKit.OpenApi.Filters;
using MicroKit.OpenApi.Internal;
using Microsoft.OpenApi;
using Xunit;

namespace MicroKit.OpenApi.Tests.Internal;

public sealed class FilterRegistryTests
{
    private sealed class DocFilterA : IOpenApiDocumentFilter
    {
        public Task ApplyAsync(OpenApiDocument document, DocumentFilterContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class DocFilterB : IOpenApiDocumentFilter
    {
        public Task ApplyAsync(OpenApiDocument document, DocumentFilterContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class OpFilterA : IOpenApiOperationFilter
    {
        public Task ApplyAsync(OpenApiOperation operation, OperationFilterContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SchemaFilterA : IOpenApiSchemaFilter
    {
        public Task ApplyAsync(OpenApiSchema schema, SchemaFilterContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static FilterRegistry Create() => new();

    [Fact]
    public void DocumentFilters_Initially_Empty()
    {
        var registry = Create();
        Assert.Empty(registry.DocumentFilters);
    }

    [Fact]
    public void OperationFilters_Initially_Empty()
    {
        var registry = Create();
        Assert.Empty(registry.OperationFilters);
    }

    [Fact]
    public void SchemaFilters_Initially_Empty()
    {
        var registry = Create();
        Assert.Empty(registry.SchemaFilters);
    }

    [Fact]
    public void AddDocumentFilter_RegistersType()
    {
        var registry = Create();
        registry.AddDocumentFilter<DocFilterA>();

        Assert.Single(registry.DocumentFilters);
        Assert.Equal(typeof(DocFilterA), registry.DocumentFilters[0]);
    }

    [Fact]
    public void AddDocumentFilter_NoDuplicates()
    {
        var registry = Create();
        registry.AddDocumentFilter<DocFilterA>();
        registry.AddDocumentFilter<DocFilterA>();

        Assert.Single(registry.DocumentFilters);
    }

    [Fact]
    public void AddDocumentFilter_MultipleDistinctTypes_AllRegistered()
    {
        var registry = Create();
        registry.AddDocumentFilter<DocFilterA>();
        registry.AddDocumentFilter<DocFilterB>();

        Assert.Equal(2, registry.DocumentFilters.Count);
    }

    [Fact]
    public void AddOperationFilter_RegistersType()
    {
        var registry = Create();
        registry.AddOperationFilter<OpFilterA>();

        Assert.Single(registry.OperationFilters);
        Assert.Equal(typeof(OpFilterA), registry.OperationFilters[0]);
    }

    [Fact]
    public void AddOperationFilter_NoDuplicates()
    {
        var registry = Create();
        registry.AddOperationFilter<OpFilterA>();
        registry.AddOperationFilter<OpFilterA>();

        Assert.Single(registry.OperationFilters);
    }

    [Fact]
    public void AddSchemaFilter_RegistersType()
    {
        var registry = Create();
        registry.AddSchemaFilter<SchemaFilterA>();

        Assert.Single(registry.SchemaFilters);
        Assert.Equal(typeof(SchemaFilterA), registry.SchemaFilters[0]);
    }

    [Fact]
    public void AddSchemaFilter_NoDuplicates()
    {
        var registry = Create();
        registry.AddSchemaFilter<SchemaFilterA>();
        registry.AddSchemaFilter<SchemaFilterA>();

        Assert.Single(registry.SchemaFilters);
    }

    [Fact]
    public void DocumentFilters_IsReadOnly()
    {
        var registry = Create();
        registry.AddDocumentFilter<DocFilterA>();

        Assert.IsAssignableFrom<IReadOnlyList<Type>>(registry.DocumentFilters);
    }
}
