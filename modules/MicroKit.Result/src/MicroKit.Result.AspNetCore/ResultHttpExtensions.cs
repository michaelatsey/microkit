using Microsoft.AspNetCore.Http;

namespace MicroKit.Result.AspNetCore;

/// <summary>
/// Extension methods for converting <see cref="Result"/> and <see cref="Result{T}"/>
/// to ASP.NET Core <see cref="IResult"/> responses.
/// </summary>
/// <example>
/// <code>
/// app.MapGet("/users/{id}", async (Guid id, IUserService svc) =>
///     (await svc.GetUserAsync(id)).ToHttpResult());
/// </code>
/// </example>
public static class ResultHttpExtensions
{
    /// <summary>
    /// Converts a non-generic result to an HTTP response.
    /// Success returns 204 No Content. Failure maps via <see cref="ErrorCategory"/>.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>An <see cref="IResult"/> representing the HTTP response.</returns>
    public static IResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess)
            return Results.NoContent();

        var problemDetails = ResultProblemDetailsFactory.CreateProblemDetails(result.Error);
        return Results.Problem(problemDetails);
    }

    /// <summary>
    /// Converts a generic result to an HTTP response.
    /// Success returns 200 OK with the value. Failure maps via <see cref="ErrorCategory"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An <see cref="IResult"/> representing the HTTP response.</returns>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        var problemDetails = ResultProblemDetailsFactory.CreateProblemDetails(result.Error);
        return Results.Problem(problemDetails);
    }
}
