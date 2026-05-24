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

    // ── Filter methods ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new collection containing only errors whose category matches <paramref name="category"/>.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    public ErrorCollection WithCategory(ErrorCategory category) =>
        From(_errors.Where(e => e.Category == category));

    /// <summary>
    /// Returns a new collection containing only errors whose severity matches <paramref name="severity"/>.
    /// </summary>
    /// <param name="severity">The severity to filter by.</param>
    public ErrorCollection WithSeverity(ErrorSeverity severity) =>
        From(_errors.Where(e => e.Severity == severity));

    /// <summary>
    /// Returns a new collection containing only errors whose code matches <paramref name="code"/>.
    /// </summary>
    /// <param name="code">The error code to filter by.</param>
    public ErrorCollection WithCode(ErrorCode code) =>
        From(_errors.Where(e => e.Code == code));

    /// <summary>
    /// Returns <see langword="true"/> if any error in this collection has the specified category.
    /// </summary>
    /// <param name="category">The category to test for.</param>
    public bool HasCategory(ErrorCategory category) =>
        _errors.Any(e => e.Category == category);

    /// <summary>
    /// Returns a new collection containing only errors of the concrete type <typeparamref name="TError"/>.
    /// </summary>
    /// <typeparam name="TError">The concrete error type to filter by.</typeparam>
    public ErrorCollection OfType<TError>() where TError : IError =>
        From(_errors.OfType<TError>().Cast<IError>());

    /// <summary>
    /// Returns a new flat collection with all nested <see cref="ErrorCollection"/> instances expanded.
    /// </summary>
    public ErrorCollection Flatten()
    {
        var flat = new List<IError>(_errors.Length);
        foreach (var error in _errors)
        {
            if (error is ErrorCollection nested)
                flat.AddRange(nested.Flatten());
            else
                flat.Add(error);
        }
        return From(flat);
    }

    /// <summary>
    /// Groups errors by their <see cref="IError.Code"/>.
    /// </summary>
    /// <returns>A dictionary mapping each error code to the list of matching errors.</returns>
    public IReadOnlyDictionary<ErrorCode, IReadOnlyList<IError>> GroupByCode()
    {
        var groups = new Dictionary<ErrorCode, List<IError>>();
        foreach (var error in _errors)
        {
            if (!groups.TryGetValue(error.Code, out var list))
                groups[error.Code] = list = [];
            list.Add(error);
        }
        return groups.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IError>)kvp.Value.AsReadOnly());
    }

    /// <summary>
    /// Groups errors by their <see cref="IError.Category"/>.
    /// </summary>
    /// <returns>A dictionary mapping each error category to the list of matching errors.</returns>
    public IReadOnlyDictionary<ErrorCategory, IReadOnlyList<IError>> GroupByCategory()
    {
        var groups = new Dictionary<ErrorCategory, List<IError>>();
        foreach (var error in _errors)
        {
            if (!groups.TryGetValue(error.Category, out var list))
                groups[error.Category] = list = [];
            list.Add(error);
        }
        return groups.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IError>)kvp.Value.AsReadOnly());
    }

    // ── Enumeration ───────────────────────────────────────────────────────

    /// <summary>Returns an enumerator that iterates through the error collection.</summary>
    public ImmutableArray<IError>.Enumerator GetEnumerator() => _errors.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<IError> IEnumerable<IError>.GetEnumerator() =>
        ((IEnumerable<IError>)_errors).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)_errors).GetEnumerator();
}
