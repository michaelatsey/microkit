namespace MicroKit.Security.AspNetCore.Extensions;

using Microsoft.AspNetCore.Http;
using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Core.Services;

/// <summary>
/// HttpContext extension methods for security operations.
/// </summary>
public static class HttpContextExtensions
{
    private const string ClientContextKey = "MicroKit.Security.Context";

    /// <summary>
    /// Gets the current client context from the HTTP context.
    /// </summary>
    public static IClientContext? GetClientContext(this HttpContext httpContext)
    {
        return httpContext.Items.TryGetValue(ClientContextKey, out var context)
            ? context as IClientContext
            : null;
    }

    /// <summary>
    /// Gets the current client context or throws if not available.
    /// </summary>
    public static IClientContext GetRequiredClientContext(this HttpContext httpContext)
    {
        return httpContext.GetClientContext()
            ?? throw new InvalidOperationException("Client context is not available. Ensure security middleware is configured.");
    }

    /// <summary>
    /// Gets the correlation ID from the current context.
    /// </summary>
    public static string GetCorrelationId(this HttpContext httpContext)
    {
        return httpContext.GetClientContext()?.CorrelationId
            ?? httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets the tenant ID from the current context.
    /// </summary>
    public static string? GetTenantId(this HttpContext httpContext)
    {
        return httpContext.GetClientContext()?.TenantId
            ?? httpContext.Request.Headers["X-Tenant-ID"].FirstOrDefault();
    }

    /// <summary>
    /// Gets the current user identifier.
    /// </summary>
    public static string? GetUserId(this HttpContext httpContext)
    {
        return httpContext.GetClientContext()?.Principal.Identifier;
    }

    /// <summary>
    /// Checks if the current request is authenticated.
    /// </summary>
    public static bool IsAuthenticated(this HttpContext httpContext)
    {
        return httpContext.GetClientContext()?.IsAuthenticated ?? false;
    }
}
