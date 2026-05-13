using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Validation;

namespace MicroKit.Security.Abstractions.Validator;

/// <summary>Secondary validator that cross-checks an extracted credential against an already-resolved primary principal (e.g. verifying an API key's tenant matches a JWT identity).</summary>
public interface ISecurityValidator
{
    /// <summary>Gets the authentication scheme this validator handles.</summary>
    AuthenticationScheme TargetScheme { get; }

    /// <summary>
    /// Returns a validation result indicating whether the secondary credential is consistent
    /// with the already-resolved primary principal.
    /// </summary>
    /// <param name="primaryPrincipal">The primary identity already authenticated for this request.</param>
    /// <param name="secondaryCredential">The additional credential to cross-check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        ISecurityPrincipal primaryPrincipal,
        ExtractionResult secondaryCredential,
        CancellationToken cancellationToken = default);
}
