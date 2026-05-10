namespace MicroKit.Security.Abstractions.Options;

using MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Configuration principale de la sécurité MicroKit.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "MicroKit:Security";

    // --- Logique de validation (Core) ---
    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.FirstSuccess;
    public AuthenticationScheme? DefaultScheme { get; set; }
    public int MaxCredentials { get; set; } = 3;

    // --- Performance et Audit ---
    public bool EnableAuditLogging { get; set; } = true;
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    // --- Pipeline HTTP (Middleware) ---
    public bool RespectAllowAnonymous { get; set; } = true;
    public bool RequireAuthenticatedUser { get; set; } = true;
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// Chemins exemptés d'authentification. 
    /// Fusion de tes listes pour éviter la confusion entre Excluded et Exempted.
    /// </summary>
    public List<string> ExemptedPaths { get; set; } =
    [
        "/health", "/ready", "/metrics","/.well-known",
        "/scalar", "/openapi", "/favicon.ico"
    ];

    // --- Contrat de Headers ---
    public string CorrelationIdHeader { get; set; } = "X-Correlation-ID";
    public string TenantIdHeader { get; set; } = "X-Tenant-ID";

    // --- AJOUT CRUCIAL : Mapping pour l'autorisation ---
    /// <summary>
    /// Configuration du mapping des claims (Role, Permission, Scope).
    /// </summary>
    public ClaimsMappingOptions ClaimsMapping { get; set; } = new();
}

/// <summary>
/// Configuration des types de claims pour l'autorisation.
/// </summary>
public sealed class ClaimsMappingOptions
{
    public string RoleClaim { get; set; } = "role";
    public string PermissionClaim { get; set; } = "permission";
    public string ScopeClaim { get; set; } = "scope";
}