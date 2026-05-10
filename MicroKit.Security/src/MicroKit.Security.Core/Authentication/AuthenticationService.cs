
using MicroKit.Security.Abstractions.Authentication;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Abstractions.Validator;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Security.Core.Authentication;
/// <summary>
/// Core security service implementation.
/// </summary>
public sealed class AuthenticationService(
    IAuthenticationProviderFactory providerFactory,
    IEnumerable<ISecurityValidator> validators, // Injection des validateurs
    IOptions<SecurityOptions> options,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    private readonly SecurityOptions _options = options.Value;

    private readonly Dictionary<AuthenticationScheme, ISecurityValidator> _validatorsMap =
        validators.ToDictionary(v => v.TargetScheme);

    /// <inheritdoc />
    public async ValueTask<SecurityAuthResult> AuthenticateAsync(
        IEnumerable<ExtractionResult> extractions,
        CancellationToken cancellationToken = default)

    {
        var extractionList = extractions as IList<ExtractionResult> ?? [.. extractions];

        // Application de la règle Strict (Point 2)
        if (_options.AuthenticationMode == AuthenticationMode.StrictSingleCredential
            && extractionList.Count > 1)
        {
            logger.LogWarning("Ambiguous authentication: multiple credentials detected.");
            return SecurityAuthResult.Failure("Multiple credentials detected while in Strict mode.");
        }

        // 1. RÉSOLUTION DE L'IDENTITÉ PRIMAIRE
        // On cherche le premier candidat primaire valide (souvent JWT ou ApiKey)
        var primaryCandidate = extractionList
            .OrderByDescending(x => x.Scheme == AuthenticationScheme.Jwt) // Priorité au JWT si présent
            .FirstOrDefault(x => x.IsPrimaryCandidate);

        if (primaryCandidate == null)
            return SecurityAuthResult.Failure("No primary identity candidate found.");

        var provider = providerFactory.GetProvider(primaryCandidate.Scheme);
        if (provider == null) return SecurityAuthResult.Failure($"No provider for {primaryCandidate.Scheme}");

        // Authentification de la base (Identity)
        var authResult = await provider.AuthenticateAsync(primaryCandidate.Value.AsMemory(), cancellationToken);

        if (!authResult.IsSuccess)
            return SecurityAuthResult.Failure($"Primary auth failed: {authResult.ErrorMessage}");

        logger.LogDebug(
            "Authentication successful via {Scheme} for principal {PrincipalId}",
            provider.Scheme,
            authResult.Principal?.Identifier);

        // 2. VALIDATION CONTEXTUELLE (Le "Plus" 2026)
        // On valide tous les AUTRES signaux par rapport à cette identité
        var secondarySignals = extractionList.Where(x => x != primaryCandidate);

        foreach (var signal in secondarySignals)
        {
            if (_validatorsMap.TryGetValue(signal.Scheme, out var validator))
            {
                var validatorResult = await validator.ValidateAsync(authResult.Principal!, signal, cancellationToken);

                if (!validatorResult.IsValid)
                {
                    logger.LogWarning("Security Mismatch: Signal {Scheme} is inconsistent with Identity {Id}",
                        signal.Scheme, authResult.Principal?.Identifier);

                    return SecurityAuthResult.Failure(validatorResult.ErrorMessage ?? "Security context inconsistency detected (Shadowing attempt?)");
                }
            }
        }

        // 3. SUCCÈS : Identité validée + Contexte cohérent
        return new SecurityAuthResult(
            true,
            authResult.Principal,
            primaryCandidate.Scheme,
            authResult.Metadata);

    }

}


