using MicroKit.Security.Abstractions.Authorization;
using MicroKit.Security.Abstractions.Contexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Security.AspNetCore.Attributes;

/// <summary>Requires the caller to hold at least one of the specified roles.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireRoleAttribute(params string[] roles)
    : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>Gets the roles that grant access.</summary>
    public string[] Roles { get; } = roles;

    /// <summary>Evaluates role requirements and returns 401/403 if the caller does not qualify.</summary>
    /// <param name="context">The authorization filter context.</param>
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var contextAccessor = context.HttpContext.RequestServices.GetRequiredService<IClientContextAccessor>();
        var clientContext = contextAccessor.Context;

        if (clientContext is null || !clientContext.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        if (!authService.IsAuthorized(clientContext.Principal, Roles))
        {
            context.Result = new ForbidResult();
        }

        return Task.CompletedTask;
    }
}
