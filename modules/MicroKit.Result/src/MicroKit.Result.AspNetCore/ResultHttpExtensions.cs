using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MicroKit.Result.AspNetCore;

/// <summary>
/// Extension methods converting <see cref="Result"/> and <see cref="Result{T}"/> to typed ASP.NET Core responses.
/// </summary>
/// <remarks>
/// <para>
/// Each method returns a <see cref="Results{TResult1, TResult2}"/> of <see cref="TypedResults"/>: an endpoint that
/// returns it describes its success response, with its type, to OpenAPI. <see cref="ProblemHttpResult"/> has no fixed
/// status and adds no metadata: declare the problem responses with <c>ProducesProblem(status)</c> or
/// <c>ProducesValidationProblem()</c>.
/// </para>
/// <para>
/// A failure answers RFC 9457 problem details built by <see cref="ResultProblemDetailsFactory.CreateProblemDetails"/>,
/// with the status of the error's category. They are written through the host's <c>IProblemDetailsService</c> when
/// one is registered (<c>AddProblemDetails</c>), so <c>CustomizeProblemDetails</c> and the <c>traceId</c> apply.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// app.MapGet("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =&gt;
///     (await svc.GetUserAsync(id, ct)).ToHttpResult());
/// </code>
/// </example>
public static class ResultHttpExtensions
{
    /// <summary>
    /// Converts a non-generic result to an HTTP response: 204 No Content on success, problem details on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>A typed response: <see cref="NoContent"/> or <see cref="ProblemHttpResult"/>.</returns>
    /// <exception cref="ResultException">Thrown when the result is uninitialized (<c>default</c>).</exception>
    public static Results<NoContent, ProblemHttpResult> ToHttpResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : Problem(result.Error);

    /// <summary>
    /// Converts a result to an HTTP response: 200 OK with the value on success, problem details on failure.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>A typed response: <see cref="Ok{TValue}"/> or <see cref="ProblemHttpResult"/>.</returns>
    /// <exception cref="ResultException">Thrown when the result is uninitialized (<c>default</c>).</exception>
    public static Results<Ok<T>, ProblemHttpResult> ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error);

    /// <summary>
    /// Converts a result to an HTTP response: 201 Created with the value and its location on success, problem
    /// details on failure.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="location">Builds the URI of the created resource from the value, or <see langword="null"/> for none.</param>
    /// <returns>A typed response: <see cref="Created{TValue}"/> or <see cref="ProblemHttpResult"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResultException">Thrown when the result is uninitialized (<c>default</c>).</exception>
    public static Results<Created<T>, ProblemHttpResult> ToCreatedHttpResult<T>(
        this Result<T> result, Func<T, string?> location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess
            ? TypedResults.Created(location(result.Value), result.Value)
            : Problem(result.Error);
    }

    /// <summary>
    /// Converts a result to an HTTP response: 202 Accepted with the value and the location to poll on success,
    /// problem details on failure.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="location">Builds the URI to poll from the value, or <see langword="null"/> for none.</param>
    /// <returns>A typed response: <see cref="Accepted{TValue}"/> or <see cref="ProblemHttpResult"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResultException">Thrown when the result is uninitialized (<c>default</c>).</exception>
    public static Results<Accepted<T>, ProblemHttpResult> ToAcceptedHttpResult<T>(
        this Result<T> result, Func<T, string?> location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess
            ? TypedResults.Accepted(location(result.Value), result.Value)
            : Problem(result.Error);
    }

    /// <summary>
    /// Converts a non-generic result to an HTTP response: 202 Accepted on success, problem details on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="location">The URI to poll, or <see langword="null"/> for none.</param>
    /// <returns>A typed response: <see cref="Accepted"/> or <see cref="ProblemHttpResult"/>.</returns>
    /// <exception cref="ResultException">Thrown when the result is uninitialized (<c>default</c>).</exception>
    public static Results<Accepted, ProblemHttpResult> ToAcceptedHttpResult(this Result result, string? location = null) =>
        result.IsSuccess ? TypedResults.Accepted(location) : Problem(result.Error);

    private static ProblemHttpResult Problem(IError error) =>
        TypedResults.Problem(ResultProblemDetailsFactory.CreateProblemDetails(error));
}
