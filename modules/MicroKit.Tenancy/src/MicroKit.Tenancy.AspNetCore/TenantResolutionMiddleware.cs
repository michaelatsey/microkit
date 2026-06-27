namespace MicroKit.Tenancy.AspNetCore;

using Microsoft.AspNetCore.Http;

/// <summary>
/// ASP.NET Core middleware that resolves the current tenant once per request and populates
/// <see cref="ITenantContextAccessor"/>. Requests always proceed — a failed resolution is
/// logged as a warning and leaves the tenant context unset, allowing downstream code to
/// enforce tenant requirements via authorization or filters.
/// </summary>
/// <remarks>
/// Register via <see cref="MultitenancyApplicationBuilderExtensions.UseMultitenancy"/>.
/// Place after authentication middleware so claim-based strategies see an authenticated user.
/// </remarks>
public sealed partial class TenantResolutionMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Resolves the tenant for the current request and invokes the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="resolver">Scoped tenant resolver — iterates registered strategies in priority order.</param>
    /// <param name="accessor">Scoped accessor used to publish the resolved tenant into the async context.</param>
    /// <param name="logger">Logger for observable failure diagnostics.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous middleware execution.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver resolver,
        ITenantContextAccessor accessor,
        ILogger<TenantResolutionMiddleware> logger)
    {
        var result = await resolver.ResolveAsync(context.RequestAborted).ConfigureAwait(false);

        if (result.IsSuccess)
            accessor.SetTenant(result.Value);
        else
            LogTenantNotResolved(logger, context.Request.Path.ToString());

        await next(context).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Tenant could not be resolved for request '{Path}'. Proceeding without tenant context.")]
    private static partial void LogTenantNotResolved(ILogger logger, string path);
}
