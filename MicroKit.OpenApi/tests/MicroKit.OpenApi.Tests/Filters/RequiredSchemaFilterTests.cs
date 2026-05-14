using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MicroKit.OpenApi.Filters;
using Microsoft.OpenApi;
using Xunit;

namespace MicroKit.OpenApi.Tests.Filters;

public sealed class RequiredSchemaFilterTests
{
    private static readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    private static SchemaFilterContext MakeContext(Type type)
        => new()
        {
            Type = type,
            DocumentName = "v1.0",
            ServiceProvider = _services
        };

    private sealed class ModelWithRequired
    {
        [Required]
        public string Name { get; set; } = default!;

        public string? Optional { get; set; }
    }

    private sealed class ModelWithJsonPropertyName
    {
        [Required]
        [JsonPropertyName("custom_name")]
        public string Name { get; set; } = default!;
    }

    private sealed class EmptyModel
    {
    }

    private static OpenApiSchema SchemaWithProperties(params string[] properties)
    {
        var schema = new OpenApiSchema
        {
            Properties = properties.ToDictionary(p => p, _ => (IOpenApiSchema)new OpenApiSchema()),
            Required = new HashSet<string>()
        };
        return schema;
    }

    [Fact]
    public async Task RequiredProperty_IsMarkedRequired_InSchema()
    {
        var filter = new RequiredSchemaFilter();
        var schema = SchemaWithProperties("name", "optional");

        await filter.ApplyAsync(schema, MakeContext(typeof(ModelWithRequired)));

        Assert.Contains("name", schema.Required!);
        Assert.DoesNotContain("optional", schema.Required!);
    }

    [Fact]
    public async Task JsonPropertyName_IsUsedAsKey()
    {
        var filter = new RequiredSchemaFilter();
        var schema = SchemaWithProperties("custom_name");

        await filter.ApplyAsync(schema, MakeContext(typeof(ModelWithJsonPropertyName)));

        Assert.Contains("custom_name", schema.Required!);
    }

    [Fact]
    public async Task NoProperties_DoesNotThrow()
    {
        var filter = new RequiredSchemaFilter();
        var schema = new OpenApiSchema { Properties = null };

        var ex = await Record.ExceptionAsync(() =>
            filter.ApplyAsync(schema, MakeContext(typeof(ModelWithRequired))));

        Assert.Null(ex);
    }

    [Fact]
    public async Task EmptyModel_ProducesNoRequired()
    {
        var filter = new RequiredSchemaFilter();
        var schema = SchemaWithProperties();

        await filter.ApplyAsync(schema, MakeContext(typeof(EmptyModel)));

        Assert.Empty(schema.Required!);
    }

    [Fact]
    public async Task PropertyName_IsCamelCased()
    {
        var filter = new RequiredSchemaFilter();
        var schema = SchemaWithProperties("name");

        await filter.ApplyAsync(schema, MakeContext(typeof(ModelWithRequired)));

        Assert.Contains("name", schema.Required!);
        Assert.DoesNotContain("Name", schema.Required!);
    }
}
