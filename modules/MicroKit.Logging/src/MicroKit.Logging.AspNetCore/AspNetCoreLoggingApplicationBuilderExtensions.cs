using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.AspNetCore;

/// <summary>
/// Extension methods for adding MicroKit logging middleware to the ASP.NET Core request pipeline.
/// </summary>
public static class AspNetCoreLoggingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the MicroKit correlation ID middleware to the request pipeline.
    /// </summary>
    /// <remarks>
    /// Place this call early in <c>Configure</c> / <c>Program.cs</c> — before routing,
    /// authentication, and authorization middleware — so that all downstream log statements
    /// carry the correlation context established per request.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The original <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseMicroKitLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(next =>
        {
            var middleware = new CorrelationIdMiddleware(
                next,
                app.ApplicationServices.GetRequiredService<IAsyncLogScopeFactory>(),
                app.ApplicationServices.GetRequiredService<ILogContextAccessor>(),
                app.ApplicationServices.GetRequiredService<ILogger<CorrelationIdMiddleware>>(),
                app.ApplicationServices.GetRequiredService<AspNetCoreLoggingOptions>());

            return middleware.InvokeAsync;
        });
    }
}
