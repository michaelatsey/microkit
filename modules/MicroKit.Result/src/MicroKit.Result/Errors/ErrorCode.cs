namespace MicroKit.Result;

/// <summary>
/// Represents a strongly-typed error code using hierarchical SCREAMING_SNAKE_CASE notation.
/// Convention: <c>DOMAIN.ENTITY.ACTION</c> (e.g., <c>AUTH.USER.NOT_FOUND</c>).
/// </summary>
/// <param name="Value">The string value of the error code.</param>
/// <example>
/// <code>
/// var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");
/// string raw = code; // implicit conversion
/// </code>
/// </example>
public readonly record struct ErrorCode(string Value) : IComparable<ErrorCode>
{
    // ── Predefined codes ─────────────────────────────────────────────────
    /// <summary>Generic failure code.</summary>
    public static readonly ErrorCode Failure = From("FAILURE");

    /// <summary>Generic validation failure.</summary>
    public static readonly ErrorCode Validation = From("VALIDATION");

    /// <summary>Resource not found.</summary>
    public static readonly ErrorCode NotFound = From("NOT_FOUND");

    /// <summary>Authentication required.</summary>
    public static readonly ErrorCode Unauthorized = From("UNAUTHORIZED");

    /// <summary>Access denied.</summary>
    public static readonly ErrorCode Forbidden = From("FORBIDDEN");

    /// <summary>Resource conflict.</summary>
    public static readonly ErrorCode Conflict = From("CONFLICT");

    /// <summary>Operation not supported.</summary>
    public static readonly ErrorCode NotSupported = From("NOT_SUPPORTED");

    /// <summary>Bad or malformed request.</summary>
    public static readonly ErrorCode BadRequest = From("BAD_REQUEST");

    /// <summary>Operation timed out.</summary>
    public static readonly ErrorCode Timeout = From("TIMEOUT");

    /// <summary>Internal / unclassified technical error.</summary>
    public static readonly ErrorCode Internal = From("INTERNAL");

    /// <summary>A required precondition was not met.</summary>
    public static readonly ErrorCode PreconditionFailed = From("PRECONDITION_FAILED");

    /// <summary>Service is currently unavailable.</summary>
    public static readonly ErrorCode ServiceUnavailable = From("SERVICE_UNAVAILABLE");

    /// <summary>Operation was cancelled.</summary>
    public static readonly ErrorCode Cancelled = From("CANCELLED");

    /// <summary>Rate limit exceeded.</summary>
    public static readonly ErrorCode TooManyRequests = From("TOO_MANY_REQUESTS");

    /// <summary>Unhandled exception was caught.</summary>
    public static readonly ErrorCode Exception = From("EXCEPTION");

    /// <summary>A required value was null or empty.</summary>
    public static readonly ErrorCode NullOrEmpty = From("NULL_OR_EMPTY");

    /// <summary>A value had an invalid format.</summary>
    public static readonly ErrorCode InvalidFormat = From("INVALID_FORMAT");

    /// <summary>A duplicate resource or value was detected.</summary>
    public static readonly ErrorCode Duplicate = From("DUPLICATE");

    /// <summary>An external service returned a failure.</summary>
    public static readonly ErrorCode ExternalServiceFailure = From("EXTERNAL_SERVICE_FAILURE");

    // ── Factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="ErrorCode"/> from a string value.
    /// </summary>
    /// <param name="value">The error code string.</param>
    /// <returns>A new <see cref="ErrorCode"/> instance.</returns>
    public static ErrorCode From(string value) => new(value);

    // ── Operators and conversions ─────────────────────────────────────────

    /// <summary>
    /// Implicitly converts an <see cref="ErrorCode"/> to its string representation.
    /// </summary>
    /// <param name="code">The error code to convert.</param>
    public static implicit operator string(ErrorCode code) => code.Value;

    // ── IComparable<ErrorCode> ────────────────────────────────────────────

    /// <summary>
    /// Compares this instance with another <see cref="ErrorCode"/> using ordinal string comparison.
    /// </summary>
    /// <param name="other">The other error code.</param>
    /// <returns>A negative, zero, or positive integer indicating relative order.</returns>
    public int CompareTo(ErrorCode other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    // ── Formatting ────────────────────────────────────────────────────────

    // ── Comparison operators (required by CA1036 / IComparable<T>) ───────

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(ErrorCode left, ErrorCode right) =>
        left.CompareTo(right) < 0;

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(ErrorCode left, ErrorCode right) =>
        left.CompareTo(right) <= 0;

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    public static bool operator >(ErrorCode left, ErrorCode right) =>
        left.CompareTo(right) > 0;

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(ErrorCode left, ErrorCode right) =>
        left.CompareTo(right) >= 0;

    // ── Formatting ────────────────────────────────────────────────────────

    /// <summary>Returns the string value of this error code.</summary>
    public override string ToString() => Value;
}
