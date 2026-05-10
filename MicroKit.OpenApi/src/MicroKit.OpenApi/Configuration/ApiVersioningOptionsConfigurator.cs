using MicroKit.OpenApi.Options;

namespace MicroKit.OpenApi.Configuration;

/// <summary>
/// Configures API versioning options using MicroKitOpenApiOptions.
/// This approach avoids the BuildServiceProvider() anti-pattern by deferring
/// options resolution until the service provider is fully built.
/// </summary>
internal sealed class ApiVersioningOptionsConfigurator : IConfigureOptions<ApiVersioningOptions>
{
    private readonly MicroKitOpenApiOptions _openApiOptions;

    public ApiVersioningOptionsConfigurator(IOptions<MicroKitOpenApiOptions> openApiOptions)
    {
        ArgumentNullException.ThrowIfNull(openApiOptions);
        _openApiOptions = openApiOptions.Value;
    }

    public void Configure(ApiVersioningOptions options)
    {
        options.DefaultApiVersion = GetDefaultApiVersion(_openApiOptions.DefaultVersion);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            // Use the following, if you would like to specify the version as a custom HTTP Header.
            new QueryStringApiVersionReader(_openApiOptions.ApiVersionQueryKey),
            // Use the following, if you would like to specify the version as a custom HTTP Header.
            new HeaderApiVersionReader(_openApiOptions.ApiVersionHeaderKey),
            // Use the following, if you would like to specify the version as a Media Type Header.
            new MediaTypeApiVersionReader(_openApiOptions.ApiVersionMediaTypeKey)
        );
    }

    private static ApiVersion GetDefaultApiVersion(string apiVersion)
    {
        if (ApiVersionParser.Default.TryParse(apiVersion, out var version))
        {
            return version;
        }
        return new ApiVersion(1, 0); // Fallback
    }
}


/// <summary>
/// Configures ApiExplorerOptions using MicroKitOpenApiOptions.
/// </summary>
internal sealed class ApiExplorerOptionsConfigurator : IConfigureOptions<ApiExplorerOptions>
{
    private readonly IOptions<MicroKitOpenApiOptions> _openApiOptions;

    public ApiExplorerOptionsConfigurator(IOptions<MicroKitOpenApiOptions> openApiOptions)
    {
        _openApiOptions = openApiOptions;
    }

    public void Configure(ApiExplorerOptions options)
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    }
}
