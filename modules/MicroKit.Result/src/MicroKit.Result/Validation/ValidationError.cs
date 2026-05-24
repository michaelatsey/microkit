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
}
