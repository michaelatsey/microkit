namespace MicroKit.Security.AspNetCore.Services;

using Microsoft.AspNetCore.Http;
using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Abstractions.Contexts;

/// <summary>
/// HTTP context-aware client context accessor.
/// Stores context in HttpContext.Items for proper scoping.
/// </summary>
public sealed class HttpClientContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IClientContextAccessor, ITenantIdAccessor
{
    private const string ContextKey = "MicroKit.Security.Context";

    // Implémentation de ITenantIdAccessor
    /// <inheritdoc/>
    public string? TenantId => Context?.TenantId;
    /// <inheritdoc />
    public IClientContext? Context
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return null;
            }

            return httpContext.Items.TryGetValue(ContextKey, out var context)
                ? context as IClientContext
                : null;
        }
        set
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is not null)
            {
                httpContext.Items[ContextKey] = value;
            }
        }
    }
}
