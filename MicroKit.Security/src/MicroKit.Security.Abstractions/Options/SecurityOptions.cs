namespace MicroKit.Security.Abstractions.Options;

using MicroKit.Security.Abstractions.Enums;

/// <summary>Root configuration for MicroKit.Security.</summary>
public sealed class SecurityOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "MicroKit:Security";

    /// <summary>Gets or sets how multiple credential sources are evaluated per request.</summary>
    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.FirstSuccess;
    /// <summary>Gets or sets the default authentication scheme when none can be auto-detected.</summary>
    public AuthenticationScheme? DefaultScheme { get; set; }
    /// <summary>Gets or sets the maximum number of credential sources accepted per request.</summary>
    public int MaxCredentials { get; set; } = 3;

    /// <summary>Gets or sets whether authentication events are written to the audit log.</summary>
    public bool EnableAuditLogging { get; set; } = true;
    /// <summary>Gets or sets how long validated authentication results are cached.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets whether endpoints decorated with <c>[AllowAnonymous]</c> bypass the security middleware.</summary>
    public bool RespectAllowAnonymous { get; set; } = true;
    /// <summary>Gets or sets whether every request must be authenticated (returns 401 on failure).</summary>
    public bool RequireAuthenticatedUser { get; set; } = true;
    /// <summary>Gets or sets whether detailed error messages are surfaced in 401/403 responses (disable in production).</summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>Gets or sets paths that bypass authentication entirely.</summary>
    public List<string> ExemptedPaths { get; set; } =
    [
        "/health", "/ready", "/metrics","/.well-known",
        "/scalar", "/openapi", "/favicon.ico"
    ];

    /// <summary>Gets or sets the header name used to propagate a correlation ID.</summary>
    public string CorrelationIdHeader { get; set; } = "X-Correlation-ID";
    /// <summary>Gets or sets the header name used to pass a tenant identifier.</summary>
    public string TenantIdHeader { get; set; } = "X-Tenant-ID";

    /// <summary>Gets or sets claim-type mappings used for authorization (role, permission, scope).</summary>
    public ClaimsMappingOptions ClaimsMapping { get; set; } = new();
}

/// <summary>Configures the claim types used for role and permission authorization checks.</summary>
public sealed class ClaimsMappingOptions
{
    /// <summary>Gets or sets the claim type used for roles.</summary>
    public string RoleClaim { get; set; } = "role";
    /// <summary>Gets or sets the claim type used for permissions.</summary>
    public string PermissionClaim { get; set; } = "permission";
    /// <summary>Gets or sets the claim type used for OAuth2 scopes.</summary>
    public string ScopeClaim { get; set; } = "scope";
}
