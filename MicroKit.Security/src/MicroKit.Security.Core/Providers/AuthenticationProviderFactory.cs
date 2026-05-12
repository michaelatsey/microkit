
using MicroKit.Security.Abstractions.Enums;
using System.Collections.Frozen;

namespace MicroKit.Security.Core.Providers;
/// <summary>
/// Default implementation of authentication provider factory.
/// </summary>
public sealed class AuthenticationProviderFactory : IAuthenticationProviderFactory
{
    private readonly FrozenDictionary<AuthenticationScheme, IAuthenticationProvider> _providers;

    /// <summary>Initializes a new factory by indexing the supplied providers by their authentication scheme.</summary>
    /// <param name="providers">All registered <see cref="IAuthenticationProvider"/> instances.</param>
    public AuthenticationProviderFactory(IEnumerable<IAuthenticationProvider> providers)
    {
        _providers = providers.ToFrozenDictionary(p => p.Scheme);
    }

    /// <inheritdoc />
    public IAuthenticationProvider? GetProvider(AuthenticationScheme scheme)
    {
        return _providers.GetValueOrDefault(scheme);
    }

    /// <inheritdoc />
    public bool HasProvider(AuthenticationScheme scheme)
    {
        return _providers.ContainsKey(scheme);
    }

    /// <inheritdoc />
    public IEnumerable<AuthenticationScheme> GetRegisteredSchemes()
    {
        return _providers.Keys;
    }
}
