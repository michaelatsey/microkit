namespace MicroKit.Result;

/// <summary>
/// Extension methods for working with sequences of results.
/// </summary>
public static class ResultEnumerableExtensions
{
    /// <summary>
    /// Partitions a sequence of results into successes and failures.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="results">The results to partition.</param>
    /// <returns>A tuple containing the success values and failure errors.</returns>
    public static (IReadOnlyList<T> Successes, IReadOnlyList<IError> Failures) Partition<T>(
        this IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var successes = new List<T>();
        var failures = new List<IError>();

        foreach (var result in results)
        {
            if (result.IsSuccess)
                successes.Add(result.Value);
            else
                failures.Add(result.Error);
        }

        return (successes, failures);
    }

    /// <summary>
    /// Extracts all successful values from a sequence of results.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="results">The results to filter.</param>
    /// <returns>An enumerable of success values.</returns>
    public static IEnumerable<T> Successes<T>(this IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        foreach (var result in results)
        {
            if (result.IsSuccess)
                yield return result.Value;
        }
    }

    /// <summary>
    /// Extracts all errors from a sequence of results.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="results">The results to filter.</param>
    /// <returns>An enumerable of errors.</returns>
    public static IEnumerable<IError> Failures<T>(this IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        foreach (var result in results)
        {
            if (result.IsFailure)
                yield return result.Error;
        }
    }

    /// <summary>
    /// Applies a result-producing function to each element and collects results.
    /// Returns a single Result containing all values if all succeed,
    /// or the first failure encountered.
    /// </summary>
    /// <typeparam name="TIn">The source element type.</typeparam>
    /// <typeparam name="TOut">The result value type.</typeparam>
    /// <param name="source">The source elements.</param>
    /// <param name="selector">The result-producing function.</param>
    /// <returns>A result containing all values, or the first failure.</returns>
    public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(
        this IEnumerable<TIn> source, Func<TIn, Result<TOut>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ResultGuard.NotNull(selector);

        var values = new List<TOut>();
        foreach (var item in source)
        {
            var result = selector(item);
            if (result.IsFailure)
                return Result<IReadOnlyList<TOut>>.Failure(result.Error);
            values.Add(result.Value);
        }

        return Result<IReadOnlyList<TOut>>.Success(values);
    }
}
