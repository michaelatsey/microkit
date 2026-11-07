namespace MicroKit.Result;

/// <summary>
/// Abstract base record for typed errors. Inherit to create a domain error (one type each), or use the factories
/// (<see cref="NotFound(ErrorCode, string)"/>…) for an ad-hoc error with an explicit category.
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
    /// <summary>
    /// Gets the kind of failure: the only input to the transport's status (in <c>MicroKit.Result.AspNetCore</c>, the
    /// HTTP status) and to the category of an <see cref="ErrorCollection"/>. Default is
    /// <see cref="ErrorCategory.Technical"/>: override it, or build the error with a factory of this type.
    /// </summary>
    public virtual ErrorCategory Category => ErrorCategory.Technical;

    /// <summary>Gets the severity, for logging and observability only. Default is <see cref="ErrorSeverity.Error"/>.</summary>
    public virtual ErrorSeverity Severity => ErrorSeverity.Error;

    /// <summary>Gets in-process context (correlation ids, entity ids, retry hints), never sent to a client by default. Default is empty.</summary>
    public virtual IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;

    /// <summary>Creates an error of category <see cref="ErrorCategory.NotFound"/>: the requested resource does not exist.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.NotFound"/>.</returns>
    public static CategorizedError NotFound(ErrorCode code, string message) => Create(code, message, ErrorCategory.NotFound);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Validation"/>: the input is invalid. For an error tied to a field, which HTTP groups under <c>errors</c>, use <see cref="ValidationError"/>.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Validation"/>.</returns>
    public static CategorizedError Validation(ErrorCode code, string message) => Create(code, message, ErrorCategory.Validation);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Unauthorized"/>: the caller is not authenticated.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Unauthorized"/>.</returns>
    public static CategorizedError Unauthorized(ErrorCode code, string message) => Create(code, message, ErrorCategory.Unauthorized);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Forbidden"/>: the authenticated caller may not perform the operation.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Forbidden"/>.</returns>
    public static CategorizedError Forbidden(ErrorCode code, string message) => Create(code, message, ErrorCategory.Forbidden);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Conflict"/>: the operation conflicts with the current state.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Conflict"/>.</returns>
    public static CategorizedError Conflict(ErrorCode code, string message) => Create(code, message, ErrorCategory.Conflict);

    /// <summary>Creates an error of category <see cref="ErrorCategory.TooManyRequests"/>: the caller exceeded a rate limit.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.TooManyRequests"/>.</returns>
    public static CategorizedError TooManyRequests(ErrorCode code, string message) => Create(code, message, ErrorCategory.TooManyRequests);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Technical"/>: an internal fault of the system.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Technical"/>.</returns>
    public static CategorizedError Technical(ErrorCode code, string message) => Create(code, message, ErrorCategory.Technical);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Unavailable"/>: a service is temporarily unavailable.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Unavailable"/>.</returns>
    public static CategorizedError Unavailable(ErrorCode code, string message) => Create(code, message, ErrorCategory.Unavailable);

    /// <summary>Creates an error of category <see cref="ErrorCategory.External"/>: an external dependency failed.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.External"/>.</returns>
    public static CategorizedError External(ErrorCode code, string message) => Create(code, message, ErrorCategory.External);

    /// <summary>Creates an error of category <see cref="ErrorCategory.BusinessRule"/>: a business rule forbids the operation.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.BusinessRule"/>.</returns>
    public static CategorizedError BusinessRule(ErrorCode code, string message) => Create(code, message, ErrorCategory.BusinessRule);

    /// <summary>Creates an error of category <see cref="ErrorCategory.NotSupported"/>: the operation is not supported.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.NotSupported"/>.</returns>
    public static CategorizedError NotSupported(ErrorCode code, string message) => Create(code, message, ErrorCategory.NotSupported);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Timeout"/>: the operation exceeded its time limit.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Timeout"/>.</returns>
    public static CategorizedError Timeout(ErrorCode code, string message) => Create(code, message, ErrorCategory.Timeout);

    /// <summary>Creates an error of category <see cref="ErrorCategory.Cancelled"/>: the operation was cancelled by something other than the caller's own token.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.Cancelled"/>.</returns>
    public static CategorizedError Cancelled(ErrorCode code, string message) => Create(code, message, ErrorCategory.Cancelled);

    /// <summary>Creates an error of category <see cref="ErrorCategory.PreconditionFailed"/>: a precondition of the operation was not met.</summary>
    /// <param name="code">The error code, <c>DOMAIN.ENTITY.REASON</c> by convention.</param>
    /// <param name="message">The client-safe message; not <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is uninitialized or <paramref name="message"/> is empty or whitespace.</exception>
    /// <returns>A <see cref="CategorizedError"/> of category <see cref="ErrorCategory.PreconditionFailed"/>.</returns>
    public static CategorizedError PreconditionFailed(ErrorCode code, string message) => Create(code, message, ErrorCategory.PreconditionFailed);

    private static CategorizedError Create(ErrorCode code, string message, ErrorCategory category)
    {
        if (string.IsNullOrWhiteSpace(code.Value))
            throw new ArgumentException("The error code is uninitialized; build it with ErrorCode.From.", nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new CategorizedError(code, message, category); // the constructor checks them too
    }
}
