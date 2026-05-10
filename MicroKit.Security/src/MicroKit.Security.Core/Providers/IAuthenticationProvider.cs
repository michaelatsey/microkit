
using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.Core.Providers;
/// <summary>
/// Base interface for authentication providers.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Authentication scheme this provider handles.
    /// </summary>
    AuthenticationScheme Scheme { get; }

    /// <summary>
    /// Authenticates the provided credentials.
    /// </summary>
    /// <param name="credentials">Raw credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result.</returns>
    ValueTask<AuthenticationResult> AuthenticateAsync(
        ReadOnlyMemory<char> credentials,
        CancellationToken cancellationToken = default);
}
