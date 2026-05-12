
using MicroKit.Security.Abstractions.Authentication;
using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.AspNetCore.Extensions;
using MicroKit.Security.AspNetCore.Extraction; // Interface d'extraction
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

namespace MicroKit.Security.AspNetCore.Middleware;
/// <summary>ASP.NET Core middleware that authenticates each request, populates <see cref="IClientContextAccessor"/>, and sets <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/>.</summary>
public sealed class SecurityMiddleware(
    RequestDelegate next,
    IEnumerable<IAuthenticationExtractor> extractors, // Injection de TOUS les extracteurs
    IOptions<SecurityOptions> coreOptions,
    ILogger<SecurityMiddleware> logger)
{
    private readonly SecurityOptions _securityOptions = coreOptions.Value;

    // On trie les extracteurs une seule fois par priorité
    private readonly IReadOnlyList<IAuthenticationExtractor> _sortedExtractors =
        [.. extractors.OrderByDescending(x => x.Priority)];

    /// <summary>Processes the HTTP request through the security pipeline.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="authenticationService">Authentication service for validating extracted credentials.</param>
    /// <param name="securityContextFactory">Factory that builds the client security context.</param>
    /// <param name="clientContextAccessor">Accessor where the resolved context is stored.</param>
    /// <param name="timeProvider">Time provider for timestamping anonymous contexts.</param>
    public async Task InvokeAsync(
        HttpContext httpContext,
        IAuthenticationService authenticationService,
        ISecurityContextFactory securityContextFactory,
        IClientContextAccessor clientContextAccessor,
        TimeProvider timeProvider)
    {

        // 1. Correlation ID
        var correlationId = GetOrCreateCorrelationId(httpContext);
        httpContext.Response.Headers[_securityOptions.CorrelationIdHeader] = correlationId;

        var path = httpContext.Request.Path;
        var endpoint = httpContext.GetEndpoint();

        bool bypassSecurity = IsPathExempted(path) ||
                     (endpoint is not null && _securityOptions.RespectAllowAnonymous && IsAnonymousAllowed(endpoint));

        // 2. On vérifie d'abord les segments de route (plus rapide que les métadonnées)
        if (bypassSecurity)
        {
            await ProceedAnonymously(httpContext, clientContextAccessor, timeProvider);
            return;
        }
              
        // 3. Extraction dynamique (Mode Expert : Agnostique du schéma)

        var extractions = new List<ExtractionResult>();
        foreach (var extractor in _sortedExtractors)
        {
            var extraction = await extractor.ExtractCredentialsAsync(httpContext);
            if (extraction is not null) extractions.Add(extraction);
        }

        // 4. Fallback DefaultScheme
        if (extractions.Count == 0 && _securityOptions.DefaultScheme is not null)
        {
            extractions.Add(new ExtractionResult(null,_securityOptions.DefaultScheme.Value));
        }

        // 4. Gestion si aucun credential n'est trouvé
        if (extractions.Count == 0)
        {
            // Si on arrive ici, c'est que ce n'est pas une route exemptée
            if (!_securityOptions.RequireAuthenticatedUser)
            {
                clientContextAccessor.Context = ClientContext.Anonymous(timeProvider);
                await next(httpContext);
                return;
            }
            await WriteUnauthorizedResponse(httpContext, "Authentication required");
            return;
        }

        // 5. Authentification via le Service dédié
        var authResult = await authenticationService.AuthenticateAsync(extractions, httpContext.RequestAborted);

        if (!authResult.IsAuthenticated)
        {
            logger.LogWarning("Authentication failed for all provided schemes at {Path}", httpContext.Request.Path);
            await WriteUnauthorizedResponse(httpContext, authResult.FailureMessage ?? "Invalid credentials");
            return;
        }

        // 5. Création du contexte (La logique de Tenant est DANS le service maintenant)
        var headerTenantId = httpContext.Request.Headers[_securityOptions.TenantIdHeader].FirstOrDefault();

        // 5. Création du contexte final
        clientContextAccessor.Context = securityContextFactory.CreateContext(
            authResult.Principal!,
            authResult.Scheme,
            headerTenantId,
            correlationId,
            authResult.Metadata);

        // 6. Injection dans l'Accessor ET dans ASP.NET User (Le Pont)
        httpContext.User = MapToClaimsPrincipal(clientContextAccessor.Context.Principal, clientContextAccessor.Context.Scheme.ToString());

        await next(httpContext);
    }

    private async Task ProceedAnonymously(HttpContext context, IClientContextAccessor accessor, TimeProvider tp)
    {
        accessor.Context = ClientContext.Anonymous(context.GetCorrelationId(), tp);
        await next(context);
    }

    private bool IsPathExempted(PathString path)
    {
        // On évite LINQ ici pour la performance sur chaque requête
        for (int i = 0; i < _securityOptions.ExemptedPaths.Count; i++)
        {
            if (path.StartsWithSegments(_securityOptions.ExemptedPaths[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsAnonymousAllowed(Endpoint endpoint)
    {
        return endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }

    private static System.Security.Claims.ClaimsPrincipal MapToClaimsPrincipal(ISecurityPrincipal principal, string scheme)
    {
        var claims = new List<System.Security.Claims.Claim>();
        if (principal.Identifier != null)
            claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, principal.Identifier));

        foreach (var c in principal.Claims)
            claims.Add(new(c.Type, c.Value));

        return new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, scheme));
    }

    private string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_securityOptions.CorrelationIdHeader, out var id) && !string.IsNullOrEmpty(id))
            return id!;
        return Guid.NewGuid().ToString("N");
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message });
    }
}