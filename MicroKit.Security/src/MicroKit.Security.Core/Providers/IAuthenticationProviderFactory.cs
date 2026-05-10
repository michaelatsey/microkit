
using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.Core.Providers;
/// <summary>
/// Factory for resolving authentication providers by scheme.
/// </summary>
public interface IAuthenticationProviderFactory
{
    /// <summary>
    /// Gets the authentication provider for the specified scheme.
    /// </summary>
    /// <param name="scheme">Authentication scheme.</param>
    /// <returns>Provider instance or null if not registered.</returns>
    IAuthenticationProvider? GetProvider(AuthenticationScheme scheme);

    /// <summary>
    /// Checks if a provider is registered for the specified scheme.
    /// </summary>
    /// <param name="scheme">Authentication scheme.</param>
    /// <returns>True if a provider is registered.</returns>
    bool HasProvider(AuthenticationScheme scheme);

    /// <summary>
    /// Gets all registered authentication schemes.
    /// </summary>
    /// <returns>Collection of registered schemes.</returns>
    IEnumerable<AuthenticationScheme> GetRegisteredSchemes();
}
