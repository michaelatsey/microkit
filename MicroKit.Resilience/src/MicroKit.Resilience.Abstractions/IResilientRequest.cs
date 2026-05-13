namespace MicroKit.Resilience.Abstractions;

/// <summary>
/// Marker interface for requests that should be protected by resilience policies.
/// Implementers can specify which resilience pipeline to use.
/// </summary>
/// <remarks>
/// This interface is used by the MediatR resilience behavior to determine which
/// Polly pipeline should protect the execution of a command or query.
/// If a request implements this interface, its PipelineName property is used;
/// otherwise, the default pipeline name from configuration is applied.
/// </remarks>
public interface IResilientRequest
{
    /// <summary>
    /// Gets the name of the resilience pipeline that should protect this request.
    /// </summary>
    /// <remarks>
    /// If <c>null</c> or empty, the default pipeline name from resilience options
    /// will be used instead.
    /// </remarks>
    string? PipelineName { get; }
}
