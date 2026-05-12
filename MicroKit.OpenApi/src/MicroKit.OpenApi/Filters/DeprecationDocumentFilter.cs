using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Built-in filter that marks deprecated operations.
/// </summary>
public sealed class DeprecationDocumentFilter : IOpenApiDocumentFilter
{
    /// <inheritdoc />
    public Task ApplyAsync(OpenApiDocument document, DocumentFilterContext context, CancellationToken cancellationToken = default)
    {
        if (
            document.Info is null || 
            document.Paths is null ||
            !context.IsDeprecated)
        {
            return Task.CompletedTask;
        }

        var version = document.Info.Version;

        // Mark all operations as deprecated for deprecated API versions
        foreach (var path in document.Paths.Values)
        {
            document.Info.Description = $"⚠️ **DEPRECATED VERSION** - This API version ({version}) is deprecated and will be removed in a future release.\n\n{document.Info.Description}";

            // TODO: fix it
            if (path.Operations is not null)
            foreach (var operation in path.Operations.Values)
            {
                operation.Deprecated = true;

                if (operation.Description is null)
                {
                    operation.Description = "This operation belongs to a deprecated API version.";
                }
                else
                {
                    operation.Description = $"This operation belongs to a deprecated API version.\n\n{operation.Description}";
                }
            }
        }

        return Task.CompletedTask;
    }
}
