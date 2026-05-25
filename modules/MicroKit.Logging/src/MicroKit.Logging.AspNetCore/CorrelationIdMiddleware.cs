using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that establishes a MicroKit operation scope for every HTTP request.
/// Reads the correlation ID from the configured request header (default: <c>X-Correlation-ID</c>),
/// or auto-generates one if the header is absent. Optionally propagates the correlation ID to
/// the response header before the response body is sent.
/// </summary>
/// <remarks>
/// Add to the pipeline via
/// <see cref="AspNetCoreLoggingApplicationBuilderExtensions.UseMicroKitLogging"/>.
/// Place before routing, authentication, and authorization middleware so all downstream
/// log statements carry the correlation context.
/// </remarks>
internal sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    IAsyncLogScopeFactory scopeFactory,
    ILogContextAccessor contextAccessor,
    ILogger<CorrelationIdMiddleware> logger,
    AspNetCoreLoggingOptions options)
{
    /// <summary>
    /// Processes the HTTP request by creating a correlated operation scope for its lifetime.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        string? incomingId = ExtractCorrelationId(context);

        ValueTask<IDisposable> scopeTask = incomingId is not null
            ? scopeFactory.BeginOperationScopeAsync(incomingId, context.RequestAborted)
            : scopeFactory.BeginOperationScopeAsync(context.RequestAborted);

        using var scope = await scopeTask.ConfigureAwait(false);

        var ctx = contextAccessor.Current;

        if (logger.IsEnabled(LogLevel.Trace))
        {
            if (incomingId is not null)
                logger.CorrelationIdFromHeader(incomingId, options.CorrelationIdHeader);
            else
                logger.CorrelationIdGenerated(ctx?.CorrelationId ?? string.Empty, options.CorrelationIdHeader);
        }

        if (options.PropagateCorrelationId && ctx is not null)
            context.Response.Headers[options.CorrelationIdHeader] = ctx.CorrelationId;

        await next(context).ConfigureAwait(false);
    }

    private string? ExtractCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(options.CorrelationIdHeader, out var values))
        {
            var value = values.ToString();
            if (!string.IsNullOrEmpty(value)) return value;
        }

        // Fall back to W3C Activity baggage propagated by upstream MicroKit services.
        return Activity.Current?.GetBaggageItem(LogPropertyNames.CorrelationId);
    }
}
