namespace MicroKit.Result;

/// <summary>
/// Extension methods for combining multiple results.
/// </summary>
public static class ResultCombineExtensions
{
    /// <summary>
    /// Combines multiple results. Fails fast on the first failure.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>A single successful result, or the first failure encountered.</returns>
    public static Result Combine(params ReadOnlySpan<Result> results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
                return result;
        }
        return Result.Success();
    }

    /// <summary>
    /// Combines multiple results, collecting ALL errors into an <see cref="ErrorCollection"/>.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>A single successful result, or a failure with all collected errors.</returns>
    public static Result CombineAll(params ReadOnlySpan<Result> results)
    {
        List<IError>? errors = null;
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                errors ??= [];
                errors.Add(result.Error);
            }
        }

        return errors is null
            ? Result.Success()
            : Result.Failure(ErrorCollection.From(errors));
    }

    /// <summary>
    /// Combines two generic results into a tuple. Fails fast on the first failure.
    /// </summary>
    public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> first, Result<T2> second)
    {
        if (first.IsFailure) return Result<(T1, T2)>.Failure(first.Error);
        if (second.IsFailure) return Result<(T1, T2)>.Failure(second.Error);
        return Result<(T1, T2)>.Success((first.Value, second.Value));
    }

    /// <summary>
    /// Combines three generic results into a tuple. Fails fast on the first failure.
    /// </summary>
    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(
        Result<T1> first, Result<T2> second, Result<T3> third)
    {
        if (first.IsFailure) return Result<(T1, T2, T3)>.Failure(first.Error);
        if (second.IsFailure) return Result<(T1, T2, T3)>.Failure(second.Error);
        if (third.IsFailure) return Result<(T1, T2, T3)>.Failure(third.Error);
        return Result<(T1, T2, T3)>.Success((first.Value, second.Value, third.Value));
    }

    /// <summary>
    /// Combines multiple generic results, collecting ALL errors.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>A single successful result, or a failure with all collected errors.</returns>
    public static Result CombineAll<T>(params ReadOnlySpan<Result<T>> results)
    {
        List<IError>? errors = null;
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                errors ??= [];
                errors.Add(result.Error);
            }
        }

        return errors is null
            ? Result.Success()
            : Result.Failure(ErrorCollection.From(errors));
    }
}
