namespace MicroKit.Result;

/// <summary>
/// LINQ query syntax support for <see cref="Result{T}"/>.
/// Enables <c>from x in result select ...</c> and <c>from x in r1 from y in r2 select ...</c>.
/// </summary>
/// <example>
/// <code>
/// var result =
///     from user in GetUser(id)
///     from cart in GetCart(user.CartId)
///     from order in CreateOrder(cart)
///     select order.ToDto();
/// </code>
/// </example>
public static class ResultLinqExtensions
{
    /// <summary>
    /// Enables the LINQ <c>select</c> clause on <see cref="Result{T}"/>.
    /// Equivalent to <see cref="ResultExtensions.Map{TIn,TOut}"/>.
    /// </summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The projected value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="selector">The projection function.</param>
    /// <returns>A result with the projected value, or the original failure.</returns>
    public static Result<TOut> Select<TIn, TOut>(
        this Result<TIn> result, Func<TIn, TOut> selector)
        => result.Map(selector);

    /// <summary>
    /// Enables the LINQ <c>from ... from ... select</c> (monadic bind) on <see cref="Result{T}"/>.
    /// </summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TMid">The intermediate result value type.</typeparam>
    /// <typeparam name="TOut">The final projected value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="collectionSelector">The function producing an intermediate result.</param>
    /// <param name="resultSelector">The function combining source and intermediate values.</param>
    /// <returns>A result with the combined value, or the first failure encountered.</returns>
    public static Result<TOut> SelectMany<TIn, TMid, TOut>(
        this Result<TIn> result,
        Func<TIn, Result<TMid>> collectionSelector,
        Func<TIn, TMid, TOut> resultSelector)
    {
        ResultGuard.NotNull(collectionSelector);
        ResultGuard.NotNull(resultSelector);

        if (result.IsFailure)
            return Result<TOut>.Failure(result.Error);

        var mid = collectionSelector(result.Value);
        if (mid.IsFailure)
            return Result<TOut>.Failure(mid.Error);

        return Result<TOut>.Success(resultSelector(result.Value, mid.Value));
    }

    /// <summary>
    /// Enables simple <c>from ... from ...</c> chaining without projection.
    /// Equivalent to <see cref="ResultExtensions.Bind{TIn,TOut}"/>.
    /// </summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="selector">The function producing a new result.</param>
    /// <returns>The result from the selector, or the original failure.</returns>
    public static Result<TOut> SelectMany<TIn, TOut>(
        this Result<TIn> result, Func<TIn, Result<TOut>> selector)
        => result.Bind(selector);
}
