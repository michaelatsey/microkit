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
public readonly record struct ErrorCode(string Value)
{
    /// <summary>
    /// Creates an <see cref="ErrorCode"/> from a string value.
    /// </summary>
    /// <param name="value">The error code string.</param>
    /// <returns>A new <see cref="ErrorCode"/> instance.</returns>
    public static ErrorCode From(string value) => new(value);

    /// <summary>
    /// Implicitly converts an <see cref="ErrorCode"/> to its string representation.
    /// </summary>
    /// <param name="code">The error code to convert.</param>
    public static implicit operator string(ErrorCode code) => code.Value;

    /// <summary>
    /// Returns the string value of this error code.
    /// </summary>
    public override string ToString() => Value;
}
