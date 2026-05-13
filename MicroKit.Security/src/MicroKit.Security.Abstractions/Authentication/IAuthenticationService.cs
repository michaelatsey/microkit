using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Authentication;

/// <summary>Runs all registered authentication provider implementations against the extracted credentials and returns the best matching result.</summary>
public interface IAuthenticationService
{
    /// <summary>Authenticates the request using all available providers.</summary>
    /// <param name="extractions">Credentials extracted from the HTTP request by <c>IAuthenticationExtractor</c> implementations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication result for the best-matched credential.</returns>
    ValueTask<SecurityAuthResult> AuthenticateAsync(
        IEnumerable<ExtractionResult> extractions,
        CancellationToken cancellationToken = default);
}
