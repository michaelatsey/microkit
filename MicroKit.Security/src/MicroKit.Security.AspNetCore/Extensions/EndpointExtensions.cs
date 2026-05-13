namespace MicroKit.Security.AspNetCore.Extensions;

using MicroKit.Security.Abstractions.Authorization;
using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using IAuthorizationService = Abstractions.Authorization.IAuthorizationService;

/// <summary>
/// Extension methods for securing Minimal API endpoints with the MicroKit framework.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Requires the user to be authenticated.
    /// </summary>
    public static TBuilder RequireAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var contextAccessor = context.HttpContext.RequestServices.GetRequiredService<IClientContextAccessor>();

            if (contextAccessor.Context is null || !contextAccessor.Context.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            return await next(context);
        });

        return builder;
    }

    /// <summary>
    /// Requires the user to hold at least one of the specified permissions (OR logic).
    /// </summary>
    public static TBuilder RequirePermissions<TBuilder>(
        this TBuilder builder,
        params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
            var contextAccessor = context.HttpContext.RequestServices.GetRequiredService<IClientContextAccessor>();

            if (contextAccessor.Context is null || !contextAccessor.Context.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            if (!authService.IsAuthorized(contextAccessor.Context.Principal, permissions))
            {
                return Results.Forbid();
            }

            return await next(context);
        });

        return builder;
    }

    /// <summary>
    /// Requires the user to hold at least one of the specified roles.
    /// </summary>
    public static TBuilder RequireRoles<TBuilder>(
        this TBuilder builder,
        params string[] roles)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
            var contextAccessor = context.HttpContext.RequestServices.GetRequiredService<IClientContextAccessor>();

            if (contextAccessor.Context is null || !contextAccessor.Context.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            if (!authService.IsAuthorized(contextAccessor.Context.Principal, roles))
            {
                return Results.Forbid();
            }

            return await next(context);
        });

        return builder;
    }
}
