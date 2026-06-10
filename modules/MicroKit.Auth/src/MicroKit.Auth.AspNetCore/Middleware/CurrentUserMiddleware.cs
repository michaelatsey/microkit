namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// Middleware that maps the authenticated <see cref="ClaimsPrincipal"/> from
/// <see cref="HttpContext.User"/> to an <see cref="ICurrentUser"/> and stores it in
/// <see cref="ICurrentUserAccessor"/> for the current request scope.
/// </summary>
/// <remarks>
/// <para>
/// Must be placed <b>after</b> <c>UseAuthentication()</c> and <b>before</b>
/// <c>UseAuthorization()</c> so that the claims principal is authenticated before
/// mapping and downstream permission checks can read the current user.
/// </para>
/// <para>
/// Unauthenticated requests proceed without setting the accessor.
/// <see cref="ISecurityContext"/> falls back to <c>CurrentUser.Anonymous</c> automatically.
/// </para>
/// <para>
/// Mapping failures are logged as warnings; the request proceeds without a current user,
/// and authorization will fail normally if a <c>[RequirePermission]</c> attribute is present.
/// </para>
/// <para>
/// Scoped dependencies (<see cref="ICurrentUserAccessor"/>, <see cref="IClaimsMapper"/>,
/// <see cref="ILogger{T}"/>) are resolved via <c>InvokeAsync</c> method injection — never
/// in the constructor — to prevent captive-dependency issues from middleware's Singleton lifetime.
/// </para>
/// </remarks>
public sealed partial class CurrentUserMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Invokes the middleware, mapping claims to the security context when the request is authenticated.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="accessor">Scoped accessor for the current user.</param>
    /// <param name="mapper">Mapper that converts a <see cref="ClaimsPrincipal"/> to <see cref="ICurrentUser"/>.</param>
    /// <param name="logger">Logger for observable mapping diagnostics.</param>
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserAccessor accessor,
        IClaimsMapper mapper,
        ILogger<CurrentUserMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var result = mapper.MapFromClaims(context.User);

            if (result.IsSuccess)
                accessor.Set(result.Value);
            else
                LogMappingFailed(logger, $"{result.Error}");
        }

        await next(context).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Claims mapping failed for authenticated principal: {Error}. Proceeding without current user.")]
    private static partial void LogMappingFailed(ILogger logger, string error);
}
