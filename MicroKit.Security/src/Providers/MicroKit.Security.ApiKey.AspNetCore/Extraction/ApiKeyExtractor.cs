using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.ApiKey.Options;
using MicroKit.Security.AspNetCore.Extraction;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace MicroKit.Security.ApiKey.AspNetCore.Extraction;

internal sealed class ApiKeyExtractor(IOptions<ApiKeyOptions> options)
    : IAuthenticationExtractor
{
    // On met IsPrimaryCandidate à false pour les extractions vides
    private readonly ExtractionResult _nullCredentials = new(string.Empty, AuthenticationScheme.None, false);

    // Priorité 90 : Légèrement inférieure au JWT (100).
    // Permet au JWT de prendre le dessus comme "Identity" s'ils sont envoyés ensemble.
    public int Priority => 90;

    public ValueTask<ExtractionResult> ExtractCredentialsAsync(HttpContext context)
    {
        var opt = options.Value.Extraction;

        // 1. Check Header Standard (X-Api-Key)
        if (context.Request.Headers.TryGetValue(opt.HeaderName, out var headerValue))
        {
            return ValueTask.FromResult(new ExtractionResult(headerValue, AuthenticationScheme.ApiKey, IsPrimaryCandidate: true));
        }

        // 2. Check Header Authorization (Authorization: ApiKey ...)
        string? authHeader = context.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(authHeader) &&
            authHeader.StartsWith(opt.AuthorizationScheme, StringComparison.OrdinalIgnoreCase))
        {
            // On skip le nom du scheme + l'espace
            var key = authHeader[(opt.AuthorizationScheme.Length + 1)..].Trim();
            return ValueTask.FromResult(new ExtractionResult(key, AuthenticationScheme.ApiKey, IsPrimaryCandidate: true));
        }

        // 3. Check Query String (Optionnel)
        if (!string.IsNullOrEmpty(opt.QueryParameterName) &&
            context.Request.Query.TryGetValue(opt.QueryParameterName, out var queryValue))
        {
            return ValueTask.FromResult(new ExtractionResult(queryValue, AuthenticationScheme.ApiKey, IsPrimaryCandidate: true));
        }
        // 4. Échec complet : On retourne l'objet pré-alloué
        return ValueTask.FromResult(_nullCredentials);
    }
}