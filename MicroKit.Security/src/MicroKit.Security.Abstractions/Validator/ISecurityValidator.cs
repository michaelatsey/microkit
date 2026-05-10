using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Validation;

namespace MicroKit.Security.Abstractions.Validator;

public interface ISecurityValidator
{
    // Retourne true si le credential secondaire est cohérent avec le principal déjà extrait
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        ISecurityPrincipal primaryPrincipal,
        ExtractionResult secondaryCredential,
        CancellationToken cancellationToken = default);

    AuthenticationScheme TargetScheme { get; }
}
