using Microsoft.OpenApi;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Built-in filter that marks properties with [Required] attribute as required in schema.
/// </summary>
public sealed class RequiredSchemaFilter : IOpenApiSchemaFilter
{
    /// <inheritdoc />
    public Task ApplyAsync(OpenApiSchema schema, SchemaFilterContext context, CancellationToken cancellationToken = default)
    {
        if (schema.Properties is null || !schema.Properties.Any())
        {
            return Task.CompletedTask;
        }

        var requiredProperties = context.Type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RequiredAttribute>() is not null)
            .Select(p => GetJsonPropertyName(p))
            .ToList();

        foreach (var propertyName in requiredProperties)
        {
            if (schema.Required is not null && !schema.Required.Contains(propertyName))
            {
                schema.Required.Add(propertyName);
            }
        }

        return Task.CompletedTask;
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        // Check for JsonPropertyName attribute
        var jsonPropertyName = property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
        if (jsonPropertyName is not null)
        {
            return jsonPropertyName.Name;
        }

        // Use camelCase by default
        return ToCamelCase(property.Name);
        
    }
    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
