using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Filters;

/// <summary>
/// Built-in document filter that marks all operations as deprecated for deprecated API versions.
/// </summary>
public sealed class DeprecationDocumentFilter : IOpenApiDocumentFilter
{
    /// <inheritdoc />
    public Task ApplyAsync(OpenApiDocument document, DocumentFilterContext context, CancellationToken cancellationToken = default)
    {
        if (document.Info is null || document.Paths is null || !context.IsDeprecated)
        {
            return Task.CompletedTask;
        }

        var version = document.Info.Version;

        document.Info.Description =
            $"⚠️ **DEPRECATED VERSION** — This API version ({version}) is deprecated and will be removed in a future release.\n\n"
            + document.Info.Description;

        foreach (var path in document.Paths.Values)
        {
            if (path.Operations is null)
            {
                continue;
            }

            foreach (var operation in path.Operations.Values)
            {
                operation.Deprecated = true;
                operation.Description = string.IsNullOrEmpty(operation.Description)
                    ? "This operation belongs to a deprecated API version."
                    : $"This operation belongs to a deprecated API version.\n\n{operation.Description}";
            }
        }

        return Task.CompletedTask;
    }
}
