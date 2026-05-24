namespace MicroKit.Result;

/// <summary>
/// Represents an error with a code, message, category, severity, and optional metadata.
/// Implement this interface to create domain-specific error types, or inherit from <see cref="Error"/>.
/// </summary>
public interface IError
{
    /// <summary>Gets the error code.</summary>
    ErrorCode Code { get; }

    /// <summary>Gets the human-readable error message.</summary>
    string Message { get; }

    /// <summary>Gets the error category for HTTP mapping and error handling strategies.</summary>
    ErrorCategory Category { get; }

    /// <summary>Gets the severity level.</summary>
    ErrorSeverity Severity { get; }

    /// <summary>Gets additional metadata associated with this error.</summary>
    IReadOnlyDictionary<string, object?> Metadata { get; }
}
