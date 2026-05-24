using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MicroKit.Result.AspNetCore;

/// <summary>
/// Creates RFC 9457 ProblemDetails from <see cref="IError"/> instances.
/// Maps <see cref="ErrorCategory"/> to appropriate HTTP status codes.
/// </summary>
public static class ResultProblemDetailsFactory
{
    /// <summary>
    /// Creates a <see cref="HttpValidationProblemDetails"/> instance from the specified error.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    /// <returns>A ProblemDetails instance with the appropriate status code and error details.</returns>
    public static ProblemDetails CreateProblemDetails(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var statusCode = ToStatusCode(error.Category);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = GetTitle(error.Category),
            Detail = error.Message,
            Type = $"https://httpstatuses.io/{statusCode}",
        };

        problemDetails.Extensions["errorCode"] = error.Code.Value;

        if (error is ErrorCollection collection)
        {
            var errors = new List<object>(collection.Count);
            foreach (var inner in collection)
            {
                errors.Add(new { code = inner.Code.Value, message = inner.Message });
            }
            problemDetails.Extensions["errors"] = errors;
        }

        return problemDetails;
    }

    /// <summary>
    /// Maps an <see cref="ErrorCategory"/> to its corresponding HTTP status code.
    /// </summary>
    /// <param name="category">The error category.</param>
    /// <returns>The corresponding HTTP status code.</returns>
    public static int ToStatusCode(ErrorCategory category) => category switch
    {
        ErrorCategory.NotFound => StatusCodes.Status404NotFound,
        ErrorCategory.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorCategory.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCategory.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCategory.Conflict => StatusCodes.Status409Conflict,
        ErrorCategory.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorCategory.Technical => StatusCodes.Status500InternalServerError,
        ErrorCategory.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCategory.External => StatusCodes.Status502BadGateway,
        ErrorCategory.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorCategory.NotSupported => StatusCodes.Status501NotImplemented,
        ErrorCategory.Timeout => StatusCodes.Status408RequestTimeout,
        ErrorCategory.Cancelled => 499, // Client Closed Request — de-facto standard, no StatusCodes constant
        ErrorCategory.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string GetTitle(ErrorCategory category) => category switch
    {
        ErrorCategory.NotFound => "Not Found",
        ErrorCategory.Validation => "Validation Failed",
        ErrorCategory.Unauthorized => "Unauthorized",
        ErrorCategory.Forbidden => "Forbidden",
        ErrorCategory.Conflict => "Conflict",
        ErrorCategory.TooManyRequests => "Too Many Requests",
        ErrorCategory.Technical => "Internal Server Error",
        ErrorCategory.Unavailable => "Service Unavailable",
        ErrorCategory.External => "Bad Gateway",
        ErrorCategory.BusinessRule => "Business Rule Violation",
        ErrorCategory.NotSupported => "Not Supported",
        ErrorCategory.Timeout => "Request Timeout",
        ErrorCategory.Cancelled => "Client Closed Request",
        ErrorCategory.PreconditionFailed => "Precondition Failed",
        _ => "Internal Server Error",
    };
}
