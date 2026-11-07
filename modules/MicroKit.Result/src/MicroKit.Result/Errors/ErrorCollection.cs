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
        Debug.Assert(!errors.IsEmpty, "An ErrorCollection is never empty.");
        _errors = errors;
        Severity = ErrorSeverity.Error;

        foreach (var error in errors)
        {
            if (error.Severity > Severity)
                Severity = error.Severity;
        }

        Category = SelectCategory(errors);
        Message = string.Join("; ", errors.Select(e => e.Message));
    }

    /// <summary>Gets the aggregate error code.</summary>
    public ErrorCode Code => ErrorCode.From("MULTI.ERROR");

    /// <summary>Gets the combined error message.</summary>
    public string Message { get; }

    /// <summary>
    /// Gets the collection's category: the category every contained error shares; otherwise the first category
    /// that reports a fault of the system (<see cref="ErrorCategory.Technical"/>, <see cref="ErrorCategory.Unavailable"/>,
    /// <see cref="ErrorCategory.External"/>, <see cref="ErrorCategory.NotSupported"/>, or a value the enum does not
    /// define), so a server fault is never hidden behind a client error; otherwise the first error's category.
    /// </summary>
    public ErrorCategory Category { get; }

    /// <summary>Gets the maximum severity across all contained errors.</summary>
    public ErrorSeverity Severity { get; }

    /// <summary>Gets an empty metadata dictionary.</summary>
    public IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;

    /// <summary>Gets the number of errors in this collection.</summary>
    public int Count => _errors.Length;

    /// <summary>
    /// Gets the first error in the collection. Never <see langword="null"/>: a collection is never empty.
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public static ErrorCollection From(params IError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
            throw new ArgumentException("An ErrorCollection must contain at least one error.", nameof(errors));
        return new ErrorCollection(ImmutableArray.Create(errors));
    }

    /// <summary>
    /// Creates an error collection from the specified errors.
    /// </summary>
    /// <param name="errors">The errors to include.</param>
    /// <returns>A new <see cref="ErrorCollection"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public static ErrorCollection From(IEnumerable<IError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var array = errors.ToImmutableArray();
        if (array.IsEmpty)
            throw new ArgumentException("An ErrorCollection must contain at least one error.", nameof(errors));
        return new ErrorCollection(array);
    }

    // ── Filter methods ────────────────────────────────────────────────────
    // A filter may match nothing, and an ErrorCollection is never empty: filters return a read-only list.

    /// <summary>
    /// Returns the errors whose category matches <paramref name="category"/>, in order. The list is empty when none matches.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    public IReadOnlyList<IError> WithCategory(ErrorCategory category) =>
        _errors.Where(e => e.Category == category).ToImmutableArray();

    /// <summary>
    /// Returns the errors whose severity matches <paramref name="severity"/>, in order. The list is empty when none matches.
    /// </summary>
    /// <param name="severity">The severity to filter by.</param>
    public IReadOnlyList<IError> WithSeverity(ErrorSeverity severity) =>
        _errors.Where(e => e.Severity == severity).ToImmutableArray();

    /// <summary>
    /// Returns the errors whose code matches <paramref name="code"/>, in order. The list is empty when none matches.
    /// </summary>
    /// <param name="code">The error code to filter by.</param>
    public IReadOnlyList<IError> WithCode(ErrorCode code) =>
        _errors.Where(e => e.Code == code).ToImmutableArray();

    /// <summary>
    /// Returns <see langword="true"/> if any error in this collection has the specified category.
    /// </summary>
    /// <param name="category">The category to test for.</param>
    public bool HasCategory(ErrorCategory category) =>
        _errors.Any(e => e.Category == category);

    /// <summary>
    /// Returns the errors of type <typeparamref name="TError"/>, in order. The list is empty when none matches.
    /// </summary>
    /// <typeparam name="TError">The error type to filter by.</typeparam>
    public IReadOnlyList<TError> OfType<TError>() where TError : IError =>
        _errors.OfType<TError>().ToImmutableArray();

    /// <summary>
    /// Returns a new flat collection with all nested <see cref="ErrorCollection"/> instances expanded, in order.
    /// It is never empty, since no collection is.
    /// </summary>
    public ErrorCollection Flatten()
    {
        var flat = ImmutableArray.CreateBuilder<IError>(_errors.Length);
        foreach (var error in _errors)
        {
            if (error is ErrorCollection nested)
                flat.AddRange(nested.Flatten()._errors);
            else
                flat.Add(error);
        }
        return new ErrorCollection(flat.ToImmutable());
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

    private static ErrorCategory SelectCategory(ImmutableArray<IError> errors)
    {
        var first = errors[0].Category;
        ErrorCategory? firstServerFault = null;
        var shared = true;

        foreach (var error in errors)
        {
            var category = error.Category;
            shared &= category == first;
            if (firstServerFault is null && ErrorCategoryFacts.IsServerFault(category))
                firstServerFault = category;
        }

        return shared ? first : firstServerFault ?? first;
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
