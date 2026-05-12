using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.ResolutionStrategies;

/// <summary>Resolves the tenant identifier from a named HTTP request header.</summary>
public sealed class HeaderResolutionStrategy : IHttpTenantResolutionStrategy
{
    private readonly string _headerName;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="headerName">The HTTP request header to read the tenant identifier from.</param>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public HeaderResolutionStrategy(string headerName, IHttpContextAccessor httpContextAccessor)
    {
        _headerName = headerName;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetTenantIdentifierAsync(HttpContext context)
    {
        context.Request.Headers.TryGetValue(_headerName, out var value);
        return new ValueTask<string?>(value.FirstOrDefault());
    }

    /// <inheritdoc/>
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var ctx = _httpContextAccessor.HttpContext;
        return ctx is null ? new ValueTask<string?>((string?)null) : GetTenantIdentifierAsync(ctx);
    }
}
