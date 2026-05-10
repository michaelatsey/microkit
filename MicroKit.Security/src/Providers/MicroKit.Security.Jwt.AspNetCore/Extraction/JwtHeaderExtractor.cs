using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.AspNetCore.Extraction;
using MicroKit.Security.Jwt.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MicroKit.Security.Jwt.AspNetCore.Extraction;

public sealed class JwtHeaderExtractor(IOptions<JwtOptions> options) : IAuthenticationExtractor
{
    private readonly ExtractionResult _nullCredentials = new (string.Empty, AuthenticationScheme.None, false);
    private readonly JwtOptions _options = options.Value;

    // Le schéma est maintenant dynamique, basé sur la config (ex: "Bearer")
    public string Scheme => _options.Extraction.AuthorizationScheme;
    // Haute priorité : si un JWT est présent, on le traite souvent avant l'API Key
    public int Priority => 100;

    public ValueTask<ExtractionResult> ExtractCredentialsAsync(HttpContext context)
    {
        // 1. Récupération sécurisée du header Authorization
        string authHeader = context.Request.Headers[HeaderNames.Authorization].ToString();

        if (string.IsNullOrEmpty(authHeader))
        {
            return ValueTask.FromResult(_nullCredentials);
        }

        // 2. Vérification dynamique du schéma (ex: "Bearer ")
        var schemePrefix = $"{_options.Extraction.AuthorizationScheme} ";

        if (!authHeader.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(_nullCredentials);
        }

        // 3. Extraction de la valeur brute
        var token = authHeader[schemePrefix.Length..].Trim();

        if (string.IsNullOrEmpty(token))
        {
            return ValueTask.FromResult(_nullCredentials);
        }

        var result = new ExtractionResult(
            Scheme: AuthenticationScheme.Jwt,
            Value: token,
            IsPrimaryCandidate: true 
        );

        return ValueTask.FromResult(result);
    }
}
