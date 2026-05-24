using System.Collections;

namespace MicroKit.Result;

/// <summary>
/// Represents a collection of errors that itself implements <see cref="IError"/>.
/// Uses <see cref="ImmutableArray{T}"/> internally for zero-copy iteration.
/// </summary>
public sealed class ErrorCollection : IError, IReadOnlyList<IError>
{
    private readonly ImmutableArray<IError> _errors;

    private ErrorCollection(ImmutableArray<IError> errors)
    {
        _errors = errors;
        Severity = ErrorSeverity.Error;
        ErrorCategory? category = null;

        foreach (var error in errors)
        {
            if (error.Severity > Severity)
                Severity = error.Severity;
            category ??= error.Category;
        }

        Category = category ?? ErrorCategory.Technical;
        Message = string.Join("; ", errors.Select(e => e.Message));
    }

    /// <summary>Gets the aggregate error code.</summary>
    public ErrorCode Code => ErrorCode.From("MULTI.ERROR");

    /// <summary>Gets the combined error message.</summary>
    public string Message { get; }

    /// <summary>Gets the error category from the first contained error.</summary>
    public ErrorCategory Category { get; }

    /// <summary>Gets the maximum severity across all contained errors.</summary>
    public ErrorSeverity Severity { get; }

    /// <summary>Gets an empty metadata dictionary.</summary>
    public IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;

    /// <summary>Gets the number of errors in this collection.</summary>
    public int Count => _errors.Length;

    /// <summary>
    /// Gets the first error in the collection, or <see langword="null"/> if empty.
    /// </summary>
    public IError? FirstOrDefault => _errors.IsEmpty ? null : _errors[0];

    /// <summary>Gets the error at the specified index.</summary>
    /// <param name="index">The zero-based index of the error.</param>
    public IError this[int index] => _errors[index];

    /// <summary>
    /// Creates an error collection from the specified errors.
    /// </summary>
    /// <param name="errors">The errors to include.</param>
    /// <returns>A new <see cref="ErrorCollection"/>.</returns>
    public static ErrorCollection From(params IError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new ErrorCollection(ImmutableArray.Create(errors));
    }

    /// <summary>
    /// Creates an error collection from the specified errors.
    /// </summary>
    /// <param name="errors">The errors to include.</param>
    /// <returns>A new <see cref="ErrorCollection"/>.</returns>
    public static ErrorCollection From(IEnumerable<IError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new ErrorCollection(errors.ToImmutableArray());
    }

    /// <summary>Returns an enumerator that iterates through the error collection.</summary>
    public ImmutableArray<IError>.Enumerator GetEnumerator() => _errors.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<IError> IEnumerable<IError>.GetEnumerator() =>
        ((IEnumerable<IError>)_errors).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)_errors).GetEnumerator();
}
