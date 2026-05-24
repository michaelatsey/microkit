namespace MicroKit.Result;

/// <summary>
/// A validation error for a specific property.
/// </summary>
/// <param name="PropertyName">The name of the property that failed validation.</param>
/// <param name="Message">The validation error message.</param>
/// <example>
/// <code>
/// var error = new ValidationError("Email", "Email address is invalid");
/// // error.Code == "VALIDATION.EMAIL"
/// // error.Category == ErrorCategory.Validation
/// </code>
/// </example>
public sealed record ValidationError(string PropertyName, string Message)
    : Error(ErrorCode.From($"VALIDATION.{PropertyName.ToUpperInvariant()}"), Message),
      IValidationError
{
    /// <summary>Gets the error category. Always <see cref="ErrorCategory.Validation"/>.</summary>
    public override ErrorCategory Category => ErrorCategory.Validation;

    /// <summary>Gets the error severity. Always <see cref="ErrorSeverity.Warning"/>.</summary>
    public override ErrorSeverity Severity => ErrorSeverity.Warning;

    // ── Static factories ──────────────────────────────────────────────────

    /// <summary>Creates a required-field validation error.</summary>
    /// <param name="propertyName">The name of the required property.</param>
    public static ValidationError Required(string propertyName)
        => new(propertyName, $"{propertyName} is required.");

    /// <summary>Creates a string-length validation error.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="min">The minimum allowed length.</param>
    /// <param name="max">The maximum allowed length.</param>
    public static ValidationError StringLength(string propertyName, int min, int max)
        => new(propertyName, $"{propertyName} must be between {min} and {max} characters.");

    /// <summary>Creates a minimum-length validation error.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="min">The minimum required length.</param>
    public static ValidationError MinLength(string propertyName, int min)
        => new(propertyName, $"{propertyName} must be at least {min} characters.");

    /// <summary>Creates a maximum-length validation error.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="max">The maximum allowed length.</param>
    public static ValidationError MaxLength(string propertyName, int max)
        => new(propertyName, $"{propertyName} must not exceed {max} characters.");

    /// <summary>Creates an out-of-range validation error.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="min">The minimum allowed value, or <see langword="null"/> if unbounded below.</param>
    /// <param name="max">The maximum allowed value, or <see langword="null"/> if unbounded above.</param>
    public static ValidationError OutOfRange(string propertyName, object? min = null, object? max = null)
        => new(propertyName, BuildRangeMessage(propertyName, min, max));

    /// <summary>Creates an invalid-format validation error.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="hint">Optional hint describing the expected format.</param>
    public static ValidationError InvalidFormat(string propertyName, string? hint = null)
        => new(propertyName, hint is null
            ? $"{propertyName} has an invalid format."
            : $"{propertyName} has an invalid format. Expected: {hint}");

    /// <summary>Creates an invalid email address validation error.</summary>
    /// <param name="propertyName">The property name (defaults to "Email").</param>
    public static ValidationError InvalidEmail(string propertyName = "Email")
        => new(propertyName, $"{propertyName} is not a valid email address.");

    /// <summary>Creates an invalid URL validation error.</summary>
    /// <param name="propertyName">The property name (defaults to "Url").</param>
    public static ValidationError InvalidUrl(string propertyName = "Url")
        => new(propertyName, $"{propertyName} is not a valid URL.");

    /// <summary>Creates a custom validation error with the specified message.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="message">The validation error message.</param>
    public static ValidationError Custom(string propertyName, string message)
        => new(propertyName, message);

    // ── Formatting ────────────────────────────────────────────────────────

    /// <summary>Returns a string representation of this validation error.</summary>
    public override string ToString() => $"ValidationError[{PropertyName}]: {Message}";

    // ── Private helpers ───────────────────────────────────────────────────

    private static string BuildRangeMessage(string propertyName, object? min, object? max) =>
        (min, max) switch
        {
            (not null, not null) => $"{propertyName} must be between {min} and {max}.",
            (not null, null)     => $"{propertyName} must be greater than or equal to {min}.",
            (null, not null)     => $"{propertyName} must be less than or equal to {max}.",
            _                    => $"{propertyName} is out of range.",
        };
}
