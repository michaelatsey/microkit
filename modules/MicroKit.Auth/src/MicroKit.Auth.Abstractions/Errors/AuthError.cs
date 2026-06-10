namespace MicroKit.Auth.Errors;

/// <summary>
/// Abstract base record for all MicroKit.Auth domain errors.
/// Inherit from this record to create typed auth errors that integrate
/// with the <see cref="Result{T}"/> railway.
/// </summary>
/// <remarks>
/// Follows the same pattern as <see cref="MicroKit.Result.Error"/>:
/// primary-constructor parameters are <c>Code</c> and <c>Message</c>,
/// and the <c>Category</c> override determines HTTP mapping.
/// </remarks>
/// <example>
/// <code>
/// public sealed record MyAuthError()
///     : AuthError(ErrorCode.From("AUTH.MY_DOMAIN.REASON"), "Descriptive message")
/// {
///     public override ErrorCategory Category => ErrorCategory.Forbidden;
/// }
/// </code>
/// </example>
/// <param name="Code">The structured error code, e.g. <c>ErrorCode.From("AUTH.TOKEN.INVALID")</c>.</param>
/// <param name="Message">Human-readable description of the error.</param>
public abstract record AuthError(ErrorCode Code, string Message) : Error(Code, Message);
