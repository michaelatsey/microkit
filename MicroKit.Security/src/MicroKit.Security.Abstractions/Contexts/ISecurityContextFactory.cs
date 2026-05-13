using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Contexts;

/// <summary>Factory for creating <see cref="IClientContext"/> instances after successful authentication.</summary>
public interface ISecurityContextFactory
{
    /// <summary>
    /// Creates a client context from the authenticated principal.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    /// <param name="scheme">Authentication scheme used.</param>
    /// <param name="tenantId">Optional tenant ID.</param>
    /// <param name="correlationId">Optional correlation ID.</param>
    /// <param name="metadata"></param>
    /// <returns>Client context.</returns>
    IClientContext CreateContext(
        ISecurityPrincipal principal,
        AuthenticationScheme scheme,
        string? tenantId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, object>? metadata = null);
}
