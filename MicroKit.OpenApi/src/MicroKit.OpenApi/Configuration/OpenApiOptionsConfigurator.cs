using MicroKit.OpenApi.Internal;
using MicroKit.OpenApi.Options;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace MicroKit.OpenApi.Configuration;

/// <summary>
/// Configures <see cref="OpenApiOptions"/> for each named document, attaching MicroKit transformers.
/// Uses <see cref="IConfigureNamedOptions{TOptions}"/> to avoid the <c>BuildServiceProvider()</c> anti-pattern.
/// </summary>
internal sealed class OpenApiOptionsConfigurator : IConfigureNamedOptions<OpenApiOptions>
{
    private readonly OpenApiDocumentTransformer _documentTransformer;
    private readonly OpenApiOperationTransformer _operationTransformer;
    private readonly OpenApiSchemaTransformer _schemaTransformer;
    private readonly IOptions<MicroKitOpenApiOptions> _microKitOptions;

    /// <summary>Initializes a new instance.</summary>
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

    /// <inheritdoc />
    public void Configure(string? name, OpenApiOptions options)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        // Only include endpoints whose ApiVersion matches this document's version.
        options.ShouldInclude = description =>
        {
            var endpointVersion = description.GetApiVersion();
            if (endpointVersion is null)
            {
                return false;
            }

            var docName = name.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? name[1..] : name;
            return ApiVersionParser.Default.TryParse(docName, out var documentVersion)
                && endpointVersion.Equals(documentVersion);
        };

        var microKitOpts = _microKitOptions.Value;
        var allVersions = microKitOpts.SupportedVersions
            .Concat(microKitOpts.DeprecatedVersions)
            .Select(v => $"v{v}")
            .ToHashSet();

        if (!allVersions.Contains(name))
        {
            return;
        }

        options.AddDocumentTransformer(_documentTransformer);
        options.AddOperationTransformer(_operationTransformer);
        options.AddSchemaTransformer(_schemaTransformer);
    }

    /// <inheritdoc />
    public void Configure(OpenApiOptions options)
    {
        Configure($"v{_microKitOptions.Value.DefaultVersion}", options);
    }
}
