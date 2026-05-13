using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.AspNetCore.Extraction;
using MicroKit.Security.Jwt.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MicroKit.Security.Jwt.AspNetCore.Extraction;

/// <summary>Extracts a JWT Bearer token from the HTTP Authorization header.</summary>
/// <param name="options">JWT authentication options.</param>
public sealed class JwtHeaderExtractor(IOptions<JwtOptions> options) : IAuthenticationExtractor
{
    private readonly ExtractionResult _nullCredentials = new (string.Empty, AuthenticationScheme.None, false);
    private readonly JwtOptions _options = options.Value;

    /// <summary>Gets the authorization scheme (e.g., "Bearer") used to identify JWT tokens.</summary>
    public string Scheme => _options.Extraction.AuthorizationScheme;

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public ValueTask<ExtractionResult> ExtractCredentialsAsync(HttpContext context)
    {
        string authHeader = context.Request.Headers[HeaderNames.Authorization].ToString();

        if (string.IsNullOrEmpty(authHeader))
        {
            return ValueTask.FromResult(_nullCredentials);
        }

        var schemePrefix = $"{_options.Extraction.AuthorizationScheme} ";

        if (!authHeader.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(_nullCredentials);
        }

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
