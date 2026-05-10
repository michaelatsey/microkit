namespace MicroKit.OpenApi.Constants;

/// <summary>
/// Default values and constants for MicroKit OpenAPI configuration.
/// </summary>
public static class MicroKitOpenApiDefaults
{
    /// <summary>
    /// Default configuration section name in appsettings.json.
    /// </summary>
    public const string ConfigurationSectionName = "MicroKit:OpenApi";

    /// <summary>
    /// Default API version when none is specified.
    /// </summary>
    public const string DefaultApiVersion = "1.0";

    /// <summary>
    /// Default API version header key.
    /// </summary>
    public const string DefaultApiVersionHeaderKey = "X-Api-Version";

    /// <summary>
    /// The default API version query key
    /// </summary>
    public const string DefaultApiVersionQueryKey = "api-version";

    /// <summary>
    /// The default API version media type key
    /// </summary>
    public const string DefaultApiVersionMediaTypeKey = "v";

    /// <summary>
    /// Default Scalar endpoint path.
    /// </summary>
    public const string DefaultScalarEndpointPath = "/scalar/{documentName}";

    /// <summary>
    /// Default OpenAPI document endpoint path.
    /// </summary>
    public const string DefaultOpenApiEndpointPath = "/openapi/{documentName}.json";

    /// <summary>
    /// OpenAPI specification version.
    /// </summary>
    public const string OpenApiSpecVersion = "3.1.0";

   
}
