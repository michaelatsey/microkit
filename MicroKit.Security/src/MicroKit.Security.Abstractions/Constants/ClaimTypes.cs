namespace MicroKit.Security.Abstractions.Constants;

/// <summary>
/// Standard claim type constants used across MicroKit.Security.
/// Provides typed constants to prevent typos and ensure consistency.
/// </summary>
public static class ClaimTypes
{
    /// <summary>Subject identifier.</summary>
    public const string Subject = "sub";

    /// <summary>Unique user identifier.</summary>
    public const string UserId = "user_id";

    /// <summary>Email address.</summary>
    public const string Email = "email";

    /// <summary>Verified email flag.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>Full name.</summary>
    public const string Name = "name";

    /// <summary>Given name.</summary>
    public const string GivenName = "given_name";

    /// <summary>Family name.</summary>
    public const string FamilyName = "family_name";

    /// <summary>User role.</summary>
    public const string Role = "role";

    /// <summary>Multiple roles (repeatable claim).</summary>
    public const string Roles = "roles";

    /// <summary>OAuth2 scope.</summary>
    public const string Scope = "scope";

    /// <summary>Tenant identifier.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Client (application) identifier.</summary>
    public const string ClientId = "client_id";

    /// <summary>Client (application) name.</summary>
    public const string ClientName = "client_name";

    /// <summary>Expiration timestamp.</summary>
    public const string Expiration = "exp";

    /// <summary>Issued-at timestamp.</summary>
    public const string IssuedAt = "iat";

    /// <summary>Not-before timestamp.</summary>
    public const string NotBefore = "nbf";

    /// <summary>Token issuer.</summary>
    public const string Issuer = "iss";

    /// <summary>Token audience.</summary>
    public const string Audience = "aud";

    /// <summary>JWT unique identifier.</summary>
    public const string JwtId = "jti";

    /// <summary>Permission level.</summary>
    public const string PermissionLevel = "permission_level";

    /// <summary>Specific permissions.</summary>
    public const string Permissions = "permissions";
}
