using MicroKit.OpenApi.Filters;
using MicroKit.OpenApi.Options;
using ScalarTheme = MicroKit.OpenApi.Options.ScalarTheme;

namespace MicroKit.OpenApi.Abstractions;

/// <summary>
/// Builder interface for configuring MicroKit OpenAPI services.
/// </summary>
public interface IMicroKitOpenApiBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Configures additional options.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder Configure(Action<MicroKitOpenApiOptions> configure);

    /// <summary>
    /// Adds Bearer/JWT security scheme.
    /// </summary>
    /// <param name="configure">Optional configuration for bearer security.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddBearerSecurity(Action<BearerSecurityOptions>? configure = null);

    /// <summary>
    /// Adds OAuth2 security scheme.
    /// </summary>
    /// <param name="configure">The OAuth2 configuration action.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddOAuth2Security(Action<OAuth2SecurityOptions> configure);

    /// <summary>
    /// Adds API Key security scheme.
    /// </summary>
    /// <param name="configure">Optional configuration for API key security.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddApiKeySecurity(Action<ApiKeySecurityOptions>? configure = null);

    /// <summary>
    /// Adds a custom OpenAPI document filter.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddDocumentFilter<TFilter>() where TFilter : class, IOpenApiDocumentFilter;

    /// <summary>
    /// Adds a custom OpenAPI operation filter.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddOperationFilter<TFilter>() where TFilter : class, IOpenApiOperationFilter;

    /// <summary>
    /// Adds a custom OpenAPI schema filter.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddSchemaFilter<TFilter>() where TFilter : class, IOpenApiSchemaFilter;

    /// <summary>
    /// Adds additional API version.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="deprecated">Whether the version is deprecated.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddVersion(string version, bool deprecated = false);

    /// <summary>
    /// Adds a server URL.
    /// </summary>
    /// <param name="url">The server URL.</param>
    /// <param name="description">Optional description.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder AddServer(string url, string? description = null);

    /// <summary>
    /// Configures Scalar UI options.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>The builder instance for chaining.</returns>
    IMicroKitOpenApiBuilder ConfigureScalar(Action<ScalarOptions> configure);
}

/// <summary>
/// Scalar UI configuration options.
/// </summary>
public sealed class ScalarOptions
{
    /// <summary>
    /// Gets or sets the theme.
    /// </summary>
    public ScalarTheme Theme { get; set; } = ScalarTheme.Default;

    /// <summary>
    /// Gets or sets whether dark mode is enabled by default.
    /// </summary>
    public bool DarkMode { get; set; } = false;

    /// <summary>
    /// Gets or sets the favicon path.
    /// </summary>
    public string? Favicon { get; set; }

    /// <summary>
    /// Gets or sets custom CSS.
    /// </summary>
    public string? CustomCss { get; set; }

    /// <summary>
    /// Gets or sets whether to show the download button.
    /// Uses DocumentDownloadType in Scalar.AspNetCore 2.0+.
    /// </summary>
    public bool ShowDownloadButton { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to show the sidebar.
    /// </summary>
    public bool ShowSidebar { get; set; } = true;

    /// <summary>
    /// Gets or sets whether search is enabled.
    /// </summary>
    public bool EnableSearch { get; set; } = true;
}
