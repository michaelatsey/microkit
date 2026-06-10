namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// Evaluates <see cref="PermissionAuthorizationRequirement"/> by delegating to
/// <see cref="IPermissionChecker"/> resolved from the current request's service scope.
/// Succeeds the requirement when the current user holds the required permission;
/// leaves it un-succeeded otherwise (authorization middleware returns 403).
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <b>Singleton</b> via <see cref="ServiceCollectionExtensions.AddMicroKitAuth"/>.
/// <see cref="IPermissionChecker"/> is Scoped, so it is resolved per-call from
/// <see cref="IHttpContextAccessor.HttpContext"/>.<see cref="HttpContext.RequestServices"/>
/// rather than injected directly, preventing captive dependency issues.
/// </para>
/// <para>
/// Uses <see cref="HttpContext.RequestAborted"/> as the cancellation token so long-running
/// permission store lookups respect the client's connection lifetime.
/// </para>
/// </remarks>
public sealed class PermissionAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionAuthorizationRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        var checker = httpContext.RequestServices.GetRequiredService<IPermissionChecker>();
        var result = await checker
            .HasPermissionAsync(requirement.Permission, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (result.IsSuccess && result.Value)
            context.Succeed(requirement);
    }
}
