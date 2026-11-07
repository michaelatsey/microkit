namespace MicroKit.Result;

/// <summary>
/// Represents an error with a code, message, category, severity, and optional metadata.
/// Implement this interface to create domain-specific error types, or inherit from <see cref="Error"/>.
/// </summary>
public interface IError
{
    /// <summary>Gets the stable identifier of the error, <c>DOMAIN.ENTITY.REASON</c> by convention.</summary>
    ErrorCode Code { get; }

    /// <summary>Gets the human-readable message. It reaches a client, so it never carries internal details.</summary>
    string Message { get; }

    /// <summary>Gets the kind of failure, the only input to a transport status.</summary>
    ErrorCategory Category { get; }

    /// <summary>Gets the severity, for logging and observability only.</summary>
    ErrorSeverity Severity { get; }

    /// <summary>Gets in-process context, never sent to a client by default.</summary>
    IReadOnlyDictionary<string, object?> Metadata { get; }
}
