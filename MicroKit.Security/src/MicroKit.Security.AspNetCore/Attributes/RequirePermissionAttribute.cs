namespace MicroKit.Security.AspNetCore.Attributes;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MicroKit.Security.Core.Services;
using MicroKit.Security.Abstractions.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MicroKit.Security.Abstractions.Contexts;

/// <summary>
/// Requires specific permissions for endpoint access.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(params string[] permissions)
    : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// Required permissions.
    /// </summary>
    public string[] Permissions { get; } = permissions;

    /// <summary>
    /// Whether all permissions are required (AND) or any permission (OR).
    /// </summary>
    public bool RequireAll { get; set; }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var contextAccessor = context.HttpContext.RequestServices.GetRequiredService<IClientContextAccessor>();
        var clientContext = contextAccessor.Context;

        if (clientContext is null || !clientContext.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var isAuthorized = RequireAll
            ? authService.HasAllPermissions(clientContext.Principal, Permissions)
            : authService.IsAuthorized(clientContext.Principal, Permissions);

        if (!isAuthorized)
        {
            context.Result = new ForbidResult();
        }
    }
}