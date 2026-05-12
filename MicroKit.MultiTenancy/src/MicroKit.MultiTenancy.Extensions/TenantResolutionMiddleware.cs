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

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private readonly MicroKitMultiTenancyOptions _options;
    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        IOptions<MicroKitMultiTenancyOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

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

public static class TenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseMicroKitMultiTenancy(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantResolutionMiddleware>();
    }
}