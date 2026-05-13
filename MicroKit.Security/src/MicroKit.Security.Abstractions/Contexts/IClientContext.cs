using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Contexts;

/// <summary>
/// Represents the authenticated client context for the current request.
/// Provides thread-safe access to identity information.
/// </summary>
public interface IClientContext
{
    /// <summary>
    /// Unique request/correlation identifier for distributed tracing.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Authenticated security principal containing identity and claims.
    /// </summary>
    ISecurityPrincipal Principal { get; }

    /// <summary>
    /// Authentication scheme used to authenticate this context.
    /// </summary>
    AuthenticationScheme Scheme { get; }

    /// <summary>
    /// UTC timestamp when the context was created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Tenant identifier for the current request.<br/>
    /// Defaults to the principal's TenantId but may differ in impersonation
    /// or delegation scenarios.
    /// Null if multi-tenancy is not in use.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Indicates whether the context represents an authenticated user.
    /// </summary>
    bool IsAuthenticated => Principal.IsAuthenticated;

    /// <summary>
    /// Additional metadata extracted during authentication.
    /// May contain cache, grace-period, or provider-specific flags.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }
}
