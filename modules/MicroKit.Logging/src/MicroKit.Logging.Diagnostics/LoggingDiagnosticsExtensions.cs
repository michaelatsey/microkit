using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Logging.Diagnostics;

/// <summary>
/// Extension methods for registering MicroKit.Logging diagnostics instrumentation.
/// </summary>
public static class LoggingDiagnosticsExtensions
{
    /// <summary>
    /// Registers MicroKit.Logging diagnostics instrumentation with the service collection.
    /// Call after <c>AddMicroKitLogging()</c> to document the Diagnostics package is in use.
    /// </summary>
    /// <remarks>
    /// V1: <see cref="ActivitySource"/> instances and the <see cref="System.Diagnostics.DiagnosticListener"/>
    /// are static — this method is a no-op marker. Future versions will register
    /// observer subscriptions via <see cref="System.Diagnostics.DiagnosticListener.AllListeners"/>.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddMicroKitDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
