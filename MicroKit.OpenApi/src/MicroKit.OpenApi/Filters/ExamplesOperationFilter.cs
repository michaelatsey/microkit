using Microsoft.OpenApi;
using System.Reflection;

namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Built-in filter that adds examples from attributes.
/// </summary>
public sealed class ExamplesOperationFilter : IOpenApiOperationFilter
{
    /// <inheritdoc />
    public Task ApplyAsync(OpenApiOperation operation, OperationFilterContext context, CancellationToken cancellationToken = default)
    {
        var methodInfo = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<MethodInfo>()
            .FirstOrDefault();

        if (methodInfo is null)
        {
            return Task.CompletedTask;
        }

        // Process response examples
        var responseExamples = methodInfo.GetCustomAttributes<OpenApiResponseExampleAttribute>();
        foreach (var example in responseExamples)
        {
            var statusCode = example.StatusCode.ToString();
            if (operation.Responses is not null && operation.Responses.TryGetValue(statusCode, out var response))
            {
                if (response.Content is null) continue;

                foreach (var (mediaType, content) in response.Content)
                {
                    if (content.Examples is not null && content.Examples.Count > 0)
                    {
                        continue;
                    }
                    content.Example = JsonNode.Parse(example.Example);
                }
            }
        }
        return Task.CompletedTask;
    }

}

/// <summary>
/// Attribute to specify response examples for OpenAPI documentation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpenApiResponseExampleAttribute : Attribute
{
    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the example value.
    /// </summary>
    public string Example { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenApiResponseExampleAttribute"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="example">The example JSON string.</param>
    public OpenApiResponseExampleAttribute(int statusCode, string example)
    {
        StatusCode = statusCode;
        Example = example;
    }
}
