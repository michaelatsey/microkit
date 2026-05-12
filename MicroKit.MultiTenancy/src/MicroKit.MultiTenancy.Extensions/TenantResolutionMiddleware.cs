using MicroKit.Abstractions.Contexts;
using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Attributes;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.ResolutionStrategies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Extensions;

/// <summary>ASP.NET Core middleware that resolves the current tenant and sets the tenant context for each request.</summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private readonly MicroKitMultiTenancyOptions _options;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Multi-tenancy configuration options.</param>
    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        IOptions<MicroKitMultiTenancyOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>Invokes the tenant resolution middleware.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="strategy">The strategy for extracting the tenant identifier from the request.</param>
    /// <param name="store">The store used to look up tenant details by identifier.</param>
    /// <param name="contextSetter">Sets the resolved tenant on the ambient tenant context.</param>
    /// <param name="tenantIdAccessor">Provides the tenant ID from the security identity.</param>
    public async Task InvokeAsync(
        HttpContext context,
        IHttpTenantResolutionStrategy strategy,
        ITenantStore store,
        ITenantContextSetter contextSetter,
        ITenantIdAccessor tenantIdAccessor)
    {
        // Skip tenant validation for health checks and OpenAPI endpoints
        if (ShouldSkipValidation(context))
        {
            await _next(context);
            return;
        }
        // 1. On récupère l'ID via la sécurité (La source de confiance)
        var identityTenantId = tenantIdAccessor.TenantId;

        var strategyTenantId = await strategy.GetTenantIdentifierAsync(context).ConfigureAwait(false);

        // 3. Logique de validation croisée
        string? finalIdentifier = null;

        if (!string.IsNullOrEmpty(identityTenantId) && !string.IsNullOrEmpty(strategyTenantId))
        {
            // CONFLIT : L'utilisateur essaie de se faire passer pour un autre tenant
            if (!string.Equals(identityTenantId, strategyTenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Security Breach Attempt: API Key for Tenant {IdentityTenant} used with Header for Tenant {StrategyTenant}",
                    identityTenantId, strategyTenantId);

                await Reject(context, StatusCodes.Status403Forbidden, "Tenant.Conflict",
                    "The provided tenant identifier does not match the security credentials.");
                return;
            }
            finalIdentifier = identityTenantId;
        }
        else
        {
            // On prend celui qui est disponible (priorité identité)
            finalIdentifier = identityTenantId ?? strategyTenantId;
        }

        if (!IsValidTenantId(finalIdentifier))
        {
            await Reject(context,
                StatusCodes.Status400BadRequest,
                "Tenant.Invalid",
                $"Header '{_options.HeaderName}' has invalid format.");

            return;
        }
        try
        {
            if (!string.IsNullOrEmpty(finalIdentifier))
            {
                // Pour l'instant, on suppose que le tenant est simplement identifié par son ID (string ou GUID).
                // Mais on pourrait facilement étendre cela pour récupérer un objet tenant
                // plus riche à partir d'une source de données.
                var tenant = await store.GetTenantAsync(finalIdentifier);
                if (tenant != null)
                {
                    contextSetter.SetTenant(tenant);
                }
            }
        }
        catch (Exception ex)
        {

            _logger.LogError(ex,
                "Failed to set tenant context for tenant {TenantId}",
                strategyTenantId);

            await Reject(context,
                StatusCodes.Status400BadRequest,
                "Tenant.Error",
                "Unable to resolve tenant context.");

            return;
        }

        // Continuer le pipeline de traitement de la requête
        await _next(context);
    }

    private bool ShouldSkipValidation(HttpContext context)
    {
        var path = context.Request.Path;

        if( _options.ExemptedPaths.Any(exempted => 
            path.StartsWithSegments(exempted,
                StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Skip endpoints marked with attribute
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata
            .GetMetadata<SkipTenantValidationAttribute>() != null;
    }

    private static bool IsValidTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return false;

        // Accept either a valid GUID or a non-empty string strategyTenantId
        return (Guid.TryParse(tenantId, out _) 
            || tenantId.Length <= 128);
    }

    private static async Task Reject(
        HttpContext context,
        int statusCode,
        string errorCode,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = "Tenant validation failed",
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        problem.Extensions["errorCode"] = errorCode;

        await context.Response.WriteAsJsonAsync(problem);
    }
}

/// <summary>Extension methods for adding the tenant resolution middleware to the ASP.NET Core pipeline.</summary>
public static class TenantResolutionMiddlewareExtensions
{
    /// <summary>Adds the <see cref="TenantResolutionMiddleware"/> to the request pipeline.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IApplicationBuilder UseMicroKitMultiTenancy(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantResolutionMiddleware>();
    }
}