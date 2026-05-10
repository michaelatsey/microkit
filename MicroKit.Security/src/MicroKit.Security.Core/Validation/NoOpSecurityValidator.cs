using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.Abstractions.Validator;

namespace MicroKit.Security.Core.Validation;

/// <summary>
/// Validateur neutre utilisé pour garantir que la collection de validateurs n'est jamais vide.
/// </summary>
internal sealed class NoOpSecurityValidator : ISecurityValidator
{
    public AuthenticationScheme TargetScheme => AuthenticationScheme.None;

    public ValueTask<ApiKeyValidationResult> ValidateAsync(
        ISecurityPrincipal primaryPrincipal,
        ExtractionResult secondaryCredential,
        CancellationToken ct)
        => ValueTask.FromResult(ApiKeyValidationResult.Success(AnonymousPrincipal.Instance));
}
