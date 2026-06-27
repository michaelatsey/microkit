namespace MicroKit.Tenancy.AspNetCore;

using Microsoft.AspNetCore.Builder;

/// <summary>Extension methods on <see cref="IApplicationBuilder"/> for MicroKit.Tenancy.</summary>
public static class MultitenancyApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="TenantResolutionMiddleware"/> to the request pipeline.
    /// </summary>
    /// <remarks>
    /// Place this call after <c>UseAuthentication()</c> and before <c>UseAuthorization()</c>
    /// so that claim-based resolution strategies see an authenticated user and downstream
    /// code has a populated tenant context.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for chaining.</returns>
    public static IApplicationBuilder UseMultitenancy(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
