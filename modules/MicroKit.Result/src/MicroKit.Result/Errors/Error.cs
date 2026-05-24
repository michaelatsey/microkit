namespace MicroKit.Result;

/// <summary>
/// Abstract base record for typed errors. Inherit to create domain-specific errors.
/// </summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The human-readable error message.</param>
/// <example>
/// <code>
/// public sealed record UserNotFoundError(Guid UserId)
///     : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
/// {
///     public override ErrorCategory Category => ErrorCategory.NotFound;
/// }
/// </code>
/// </example>
public abstract record Error(ErrorCode Code, string Message) : IError
{
    /// <summary>Gets the error category. Default is <see cref="ErrorCategory.Technical"/>.</summary>
    public virtual ErrorCategory Category => ErrorCategory.Technical;

    /// <summary>Gets the error severity. Default is <see cref="ErrorSeverity.Error"/>.</summary>
    public virtual ErrorSeverity Severity => ErrorSeverity.Error;

    /// <summary>Gets additional metadata. Default is empty.</summary>
    public virtual IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;
}
