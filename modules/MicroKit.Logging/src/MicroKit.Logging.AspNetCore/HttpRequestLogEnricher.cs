using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace MicroKit.Logging.AspNetCore;

/// <summary>
/// Enriches the MicroKit log scope with <see cref="LogPropertyNames.RequestId"/> sourced from
/// <see cref="HttpContext.TraceIdentifier"/>. No-op when invoked outside an active HTTP context.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically by
/// <see cref="AspNetCoreLoggingServiceCollectionExtensions.AddMicroKitAspNetCoreLogging"/>.
/// </para>
/// <para>
/// Sets <see cref="LogPropertyNames.RequestId"/> only — the correlation ID is established by
/// <see cref="CorrelationIdMiddleware"/> at scope creation and is not duplicated here.
/// </para>
/// </remarks>
public sealed class HttpRequestLogEnricher : ILogEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="HttpRequestLogEnricher"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public HttpRequestLogEnricher(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public int Order => LogEnricherOrder.Correlation;

    /// <inheritdoc/>
    public void Enrich(ILogEnrichmentContext context)
    {
        Debug.Assert(context is not null);
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null) return;

        context.TrySetProperty(LogPropertyNames.RequestId, httpContext.TraceIdentifier);
    }
}
