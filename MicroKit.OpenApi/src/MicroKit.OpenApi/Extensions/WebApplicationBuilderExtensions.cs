using MicroKit.OpenApi.Abstractions;
using MicroKit.OpenApi.Options;

namespace MicroKit.OpenApi.Extensions;

/// <summary>
/// Extension methods for WebApplicationBuilder to complete MicroKit OpenAPI setup.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// Registers OpenAPI documents for the specified versions.
    /// Call this on the builder returned by AddMicroKitOpenApi to register version documents.
    /// </summary>
    /// <param name="builder">The MicroKit OpenAPI builder.</param>
    /// <param name="versions">The API versions to register documents for.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddMicroKitOpenApi(builder.Configuration)
    ///     .WithVersionDocuments("1.0", "2.0", "0.9");
    /// </code>
    /// </example>
    public static IMicroKitOpenApiBuilder WithVersionDocuments(
        this IMicroKitOpenApiBuilder builder,
        params string[] versions)
    {
        foreach (var version in versions.Distinct())
        {
            var documentName = $"v{version}";
            builder.Services.AddOpenApi(documentName);
        }

        return builder;
    }

    /// <summary>
    /// Registers OpenAPI documents based on the configured supported and deprecated versions.
    /// This reads versions from the already-configured MicroKitOpenApiOptions.
    /// </summary>
    /// <param name="builder">The MicroKit OpenAPI builder.</param>
    /// <param name="options">The pre-configured options to read versions from.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var openApiOptions = new MicroKitOpenApiOptions();
    /// builder.Configuration.GetSection("Microkit:OpenApi").Bind(openApiOptions);
    /// 
    /// builder.Services.AddMicroKitOpenApi(builder.Configuration)
    ///     .WithVersionDocuments(openApiOptions);
    /// </code>
    /// </example>
    public static IMicroKitOpenApiBuilder WithVersionDocuments(
        this IMicroKitOpenApiBuilder builder,
        MicroKitOpenApiOptions options)
    {
        var allVersions = options.SupportedVersions
            .Concat(options.DeprecatedVersions)
            .Distinct()
            .OrderByDescending(v => v)
            .ToArray();

        return builder.WithVersionDocuments(allVersions);
    }

    /// <summary>
    /// Registers OpenAPI documents by reading configuration from IConfiguration.
    /// This binds the configuration section and registers documents for all configured versions.
    /// </summary>
    /// <param name="builder">The MicroKit OpenAPI builder.</param>
    /// <param name="configuration">The configuration to read versions from.</param>
    /// <param name="sectionName">The configuration section name (default: "Microkit:OpenApi").</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddMicroKitOpenApi(builder.Configuration)
    ///     .WithVersionDocumentsFromConfig(builder.Configuration);
    /// </code>
    /// </example>
    public static IMicroKitOpenApiBuilder WithVersionDocumentsFromConfig(
        this IMicroKitOpenApiBuilder builder,
        IConfiguration configuration,
        string sectionName = "Microkit:OpenApi")
    {
        var options = new MicroKitOpenApiOptions();
        configuration.GetSection(sectionName).Bind(options);

        // Ensure at least the default version is registered
        if (options.SupportedVersions.Count == 0 && options.DeprecatedVersions.Count == 0)
        {
            options.SupportedVersions.Add(options.DefaultVersion);
        }

        return builder.WithVersionDocuments(options);
    }
}
