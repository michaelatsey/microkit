using MediatR;
using MicroKit.Abstractions.Contexts;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MicroKit.Cqrs.MediatR.Behaviors;

/// <summary>MediatR pipeline behavior that logs request handling time and outcome, including correlation and tenant context.</summary>
/// <param name="logger">Logger for this behavior.</param>
/// <param name="correlationContext">Provides the current correlation ID.</param>
/// <param name="tenantContext">Provides the current tenant ID.</param>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICorrelationContext correlationContext,
    ITenantIdAccessor tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;

        using (logger.BeginScope(
            "RequestName={RequestName} CorrelationId={CorrelationId} TenantId={TenantId}",
            requestName, correlationContext.CorrelationId, tenantContext.TenantId ?? string.Empty))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Handling {RequestName}", requestName);
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await next(ct);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Handled {RequestName} successfully in {Elapsed}ms", requestName, sw.ElapsedMilliseconds);
                }
                return response;
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(ex, "Handled {RequestName} with error after {Elapsed}ms", requestName, sw.ElapsedMilliseconds);
                }
                throw;
            }
        }
    }
}
