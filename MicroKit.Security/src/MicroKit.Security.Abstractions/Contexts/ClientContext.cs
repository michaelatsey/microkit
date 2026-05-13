using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using System.Collections.ObjectModel;

namespace MicroKit.Security.Abstractions.Contexts;

/// <summary>
/// Immutable client context implementation using a primary constructor.
/// Sealed record to guarantee immutability and optimise performance.
/// </summary>
/// <seealso cref="MicroKit.Security.Abstractions.Contexts.IClientContext" />
/// <param name="CorrelationId">Unique correlation identifier.</param>
/// <param name="Principal">Security principal.</param>
/// <param name="Scheme">Authentication scheme used.</param>
/// <param name="TenantId">Optional tenant identifier.</param>
/// <param name="CreatedAt">Creation timestamp.</param>
/// <param name="Metadata">Additional metadata.</param>
public sealed record ClientContext(
    string CorrelationId,
    ISecurityPrincipal Principal,
    AuthenticationScheme Scheme,
    string? TenantId,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, object> Metadata) : IClientContext
{
    /// <summary>
    /// Creates an anonymous context with a new correlation identifier.
    /// </summary>
    /// <param name="timeProvider">Time provider for the creation timestamp.</param>
    /// <returns>A new anonymous client context.</returns>
    public static ClientContext Anonymous(TimeProvider timeProvider) => new(
        CorrelationId: Guid.NewGuid().ToString("N"),
        Principal: AnonymousPrincipal.Instance,
        Scheme: AuthenticationScheme.None,
        TenantId: null,
        CreatedAt: timeProvider.GetUtcNow(),
        Metadata: ReadOnlyDictionary<string, object>.Empty
    );

    /// <summary>
    /// Creates an anonymous context with the specified correlation identifier.
    /// </summary>
    /// <param name="correlationId">Correlation identifier to use.</param>
    /// <param name="timeProvider">Time provider for the creation timestamp.</param>
    /// <returns>A new anonymous client context.</returns>
    public static ClientContext Anonymous(string correlationId, TimeProvider timeProvider) => new(
        CorrelationId: correlationId,
        Principal: AnonymousPrincipal.Instance,
        Scheme: AuthenticationScheme.None,
        TenantId: null,
        CreatedAt: timeProvider.GetUtcNow(),
        Metadata: ReadOnlyDictionary<string, object>.Empty
    );

    /// <summary>
    /// Returns a new context with the specified tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <returns>New context with the updated tenant.</returns>
    public ClientContext WithTenant(string tenantId) => this with { TenantId = tenantId };

    /// <summary>
    /// Returns a new context with a different principal.
    /// </summary>
    /// <param name="principal">New principal.</param>
    /// <param name="scheme">Authentication scheme.</param>
    /// <returns>New authenticated context.</returns>
    public ClientContext WithPrincipal(ISecurityPrincipal principal, AuthenticationScheme scheme) =>
        this with { Principal = principal, Scheme = scheme };

    /// <summary>
    /// Returns a new context with the specified metadata entry added or updated.
    /// </summary>
    public ClientContext WithMetadata(string key, object value)
    {
        var newMetadata = new Dictionary<string, object>(Metadata) { [key] = value };
        return this with { Metadata = newMetadata };
    }
}
