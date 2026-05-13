using MicroKit.Resilience.Abstractions;
using System.Net.Sockets;

namespace MicroKit.Resilience.Http;

/// <summary>
/// Resilience detector for HTTP and network-related transient errors.
/// </summary>
/// <remarks>
/// This detector identifies transient HTTP exceptions and network errors
/// that should be retried, including timeouts, socket errors, and server errors (5xx).
/// </remarks>
public sealed class HttpResilienceDetector : IResilienceStrategyDetector
{
    /// <summary>
    /// Determines whether the specified exception originates from an HTTP operation or network layer.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception is related to HTTP requests, timeouts, or socket operations; otherwise, <c>false</c>.
    /// </returns>
    public bool CanHandle(Exception ex) =>
        ex is HttpRequestException ||
        ex is TaskCanceledException ||
        ex is SocketException;

    /// <summary>
    /// Determines whether an HTTP-related exception represents a transient error
    /// that should be retried.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception represents a transient HTTP error; otherwise, <c>false</c>.
    /// </returns>
    public bool ShouldRetry(Exception ex)
    {
        return ex switch
        {
            // HTTP error with server error status code (5xx) or no status code (connection failure)
            HttpRequestException httpEx when httpEx.StatusCode is null || (int)httpEx.StatusCode >= 500
                => true,

            // Task timeout (not due to explicit cancellation request)
            TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested
                => true,

            // Network socket errors (connection refused, DNS failure, network unreachable, etc.)
            SocketException
                => true,

            _ => false
        };
    }
}
