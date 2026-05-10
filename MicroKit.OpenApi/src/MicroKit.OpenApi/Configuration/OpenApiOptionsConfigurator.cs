using MicroKit.OpenApi.Internal;
using MicroKit.OpenApi.Options;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace MicroKit.OpenApi.Configuration;

/// <summary>
/// Configures OpenAPI options with transformers for each document.
/// Uses IConfigureNamedOptions to configure options by document name without BuildServiceProvider.
/// </summary>
internal sealed class OpenApiOptionsConfigurator : IConfigureNamedOptions<OpenApiOptions>
{
    private readonly OpenApiDocumentTransformer _documentTransformer;
    private readonly OpenApiOperationTransformer _operationTransformer;
    private readonly OpenApiSchemaTransformer _schemaTransformer;
    private readonly IOptions<MicroKitOpenApiOptions> _microKitOptions;

    public OpenApiOptionsConfigurator(
        OpenApiDocumentTransformer documentTransformer,
        OpenApiOperationTransformer operationTransformer,
        OpenApiSchemaTransformer schemaTransformer,
        IOptions<MicroKitOpenApiOptions> microKitOptions)
    {
        _documentTransformer = documentTransformer;
        _operationTransformer = operationTransformer;
        _schemaTransformer = schemaTransformer;
        _microKitOptions = microKitOptions;
    }

    public void Configure(string? name, OpenApiOptions options)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        // 1. Définir le filtre de sélection (Le chaînon manquant)
        // Cela dit à OpenAPI : "N'inclut que les endpoints dont le GroupName correspond au nom du document"
        options.ShouldInclude = (description) =>
        {
            // 1. Récupérer la version de l'endpoint (fournie par ApiExplorer)
            var endpointVersion = description.GetApiVersion();
            if (endpointVersion == null) return false;

            // 2. Nettoyer le nom du document (enlever le 'v' initial s'il existe)
            var docName = name.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                          ? name[1..]
                          : name;

            // 3. Transformer le nom du document en objet ApiVersion
            if (ApiVersionParser.Default.TryParse(docName, out var documentVersion))
            {
                // La magie est ici : Equals() gère les segments manquants (1.0 == 1.0.0)
                return endpointVersion.Equals(documentVersion);
            }

            return false;
            //return description.GroupName == null || string.Equals(description.GroupName, name, StringComparison.OrdinalIgnoreCase);
        };

        // Check if this document name corresponds to one of our versions
        var microKitOpts = _microKitOptions.Value;
        var allVersions = microKitOpts.SupportedVersions
            .Concat(microKitOpts.DeprecatedVersions)
            .Select(v => $"v{v}")
            .ToHashSet();

        if (!allVersions.Contains(name))
        {
            return;
        }

        // Add transformers for MicroKit-managed documents
        options.AddDocumentTransformer(_documentTransformer);
        options.AddOperationTransformer(_operationTransformer);
        options.AddSchemaTransformer(_schemaTransformer);
    }

    public void Configure(OpenApiOptions options)
    {
        // Default configuration - apply to all documents if no name specified
        Configure($"v{_microKitOptions.Value.DefaultVersion}", options);
    }
}
