namespace MicroKit.Result;

/// <summary>
/// Accumulates validation errors and produces a <see cref="Result"/> or <see cref="Result{T}"/>.
/// </summary>
/// <example>
/// <code>
/// var validation = new ValidationResult();
/// validation.AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required");
/// validation.AddErrorIf(age &lt; 0, "Age", "Age must be non-negative");
/// Result result = validation.ToResult();
/// </code>
/// </example>
public sealed class ValidationResult
{
    private readonly List<IValidationError> _errors = [];

    /// <summary>Gets a value indicating whether there are no validation errors.</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>Gets the accumulated validation errors.</summary>
    public IReadOnlyList<IValidationError> Errors => _errors.AsReadOnly();

    /// <summary>
    /// Adds a validation error.
    /// </summary>
    /// <param name="propertyName">The name of the property that failed validation.</param>
    /// <param name="message">The validation error message.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ValidationResult AddError(string propertyName, string message)
    {
        _errors.Add(new ValidationError(propertyName, message));
        return this;
    }

    /// <summary>
    /// Adds a validation error.
    /// </summary>
    /// <param name="error">The validation error to add.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ValidationResult AddError(IValidationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
        return this;
    }

    /// <summary>
    /// Adds a validation error if the condition is true.
    /// </summary>
    /// <param name="condition">The condition to check.</param>
    /// <param name="propertyName">The name of the property that failed validation.</param>
    /// <param name="message">The validation error message.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ValidationResult AddErrorIf(bool condition, string propertyName, string message)
    {
        if (condition)
            _errors.Add(new ValidationError(propertyName, message));
        return this;
    }

    /// <summary>
    /// Converts to a non-generic <see cref="Result"/>.
    /// Returns success if valid, or failure with all collected errors.
    /// </summary>
    /// <returns>A successful result or a failure containing all validation errors.</returns>
    public Result ToResult() =>
        IsValid
            ? Result.Success()
            : Result.Failure(ErrorCollection.From(_errors.Cast<IError>()));

    /// <summary>
    /// Converts to a generic <see cref="Result{T}"/> containing the specified value on success.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to return on success.</param>
    /// <returns>A successful result with the value, or a failure containing all validation errors.</returns>
    public Result<T> ToResult<T>(T value) =>
        IsValid
            ? Result<T>.Success(value)
            : Result<T>.Failure(ErrorCollection.From(_errors.Cast<IError>()));
}
