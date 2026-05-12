using MicroKit.Security.Abstractions.Authorization;
using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

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

        // Ici, on vérifie si l'utilisateur possède AU MOINS un des rôles
        // On utilise IsAuthorized qui, par défaut dans notre implémentation, 
        // vérifie RoleClaim, PermissionClaim et ScopeClaim.
        var hasRole = authService.IsAuthorized(clientContext.Principal, Roles);

        if (!hasRole)
        {
            context.Result = new ForbidResult();
        }

        await Task.CompletedTask;
    }
}
