namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// Middleware pipeline registration extensions for <c>MicroKit.Auth.AspNetCore</c>.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="CurrentUserMiddleware"/> to the request pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be called <b>after</b> <c>UseAuthentication()</c> and <b>before</b>
    /// <c>UseAuthorization()</c>. Correct ordering ensures the JWT principal is validated
    /// before claims are mapped, and the security context is populated before permission
    /// checks run.
    /// </para>
    /// <para>
    /// Typical ordering:
    /// <code>
    /// app.UseAuthentication();
    /// app.UseMicroKitAuth();   // ← maps ClaimsPrincipal → ICurrentUserAccessor
    /// app.UseAuthorization();
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UseMicroKitAuth(this IApplicationBuilder app)
    {
        app.UseMiddleware<CurrentUserMiddleware>();
        return app;
    }
}
