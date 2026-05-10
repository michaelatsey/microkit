using MediatR;
using MicroKit.Abstractions.Contexts;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MicroKit.Cqrs.MediatR.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICorrelationContext correlationContext,
    ITenantIdAccessor tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;

        // On utilise des "Scopes" de log. Serilog les transforme en propriétés JSON.
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationContext.CorrelationId,
            ["TenantId"] = tenantContext.TenantId ?? string.Empty ,
            ["RequestName"] = requestName
        }))
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
