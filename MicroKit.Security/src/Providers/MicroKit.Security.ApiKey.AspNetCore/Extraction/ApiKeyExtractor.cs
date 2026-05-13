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
    private readonly ExtractionResult _nullCredentials = new(string.Empty, AuthenticationScheme.None, false);

    // Priority 90: slightly below JWT (100) so that JWT takes precedence as the primary identity when both are present
    public int Priority => 90;

    public ValueTask<ExtractionResult> ExtractCredentialsAsync(HttpContext context)
    {
        var opt = options.Value.Extraction;

        if (context.Request.Headers.TryGetValue(opt.HeaderName, out var headerValue))
        {
            return ValueTask.FromResult(new ExtractionResult(headerValue, AuthenticationScheme.ApiKey, IsPrimaryCandidate: true));
        }

        string? authHeader = context.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(authHeader) &&
            authHeader.StartsWith(opt.AuthorizationScheme, StringComparison.OrdinalIgnoreCase))
        {
            var key = authHeader[(opt.AuthorizationScheme.Length + 1)..].Trim();
            return ValueTask.FromResult(new ExtractionResult(key, AuthenticationScheme.ApiKey, IsPrimaryCandidate: true));
        }

        if (!string.IsNullOrEmpty(opt.QueryParameterName) &&
            context.Request.Query.TryGetValue(opt.QueryParameterName, out var queryValue))
        {
            return ValueTask.FromResult(new ExtractionResult(queryValue, AuthenticationScheme.ApiKey, IsPrimaryCandidate: true));
        }

        return ValueTask.FromResult(_nullCredentials);
    }
}
