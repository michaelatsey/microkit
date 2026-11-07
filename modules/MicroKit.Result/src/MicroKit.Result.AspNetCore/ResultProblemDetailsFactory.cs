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
    /// Creates the problem details describing the specified error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the error is an <see cref="IValidationError"/>, or an <see cref="ErrorCollection"/> that holds some
    /// (nested collections included), and the status is not a server error (5xx), the result is an
    /// <see cref="HttpValidationProblemDetails"/> whose <see cref="HttpValidationProblemDetails.Errors"/> groups
    /// the validation messages by <see cref="IValidationError.PropertyName"/>, each property's messages in order;
    /// other errors are not
    /// listed there. A 5xx response never carries field errors: the client has nothing to correct. Otherwise
    /// the result is a <see cref="ProblemDetails"/>.
    /// </para>
    /// <para>
    /// <see cref="ProblemDetails.Detail"/> is <see cref="IError.Message"/>, which the error contract keeps safe for
    /// a client; <see cref="IError.Metadata"/> is never emitted. The extension <c>errorCode</c> carries
    /// <see cref="IError.Code"/>; for an <see cref="ErrorCollection"/>, the extension <c>errorCodes</c> lists the
    /// codes of its errors, nested collections flattened, in order.
    /// </para>
    /// </remarks>
    /// <param name="error">The error to convert.</param>
    /// <returns>The problem details, with the status code of the error's category.</returns>
    public static ProblemDetails CreateProblemDetails(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var statusCode = ToStatusCode(error.Category);
        var fieldErrors = statusCode < StatusCodes.Status500InternalServerError ? GroupValidationErrors(error) : null;

        var problemDetails = fieldErrors is null
            ? new ProblemDetails()
            : new HttpValidationProblemDetails(fieldErrors);

        problemDetails.Status = statusCode;
        problemDetails.Title = GetTitle(error.Category);
        problemDetails.Detail = error.Message;
        problemDetails.Type = $"https://httpstatuses.io/{statusCode}";
        problemDetails.Extensions["errorCode"] = error.Code.Value;
        if (error is ErrorCollection collection)
            problemDetails.Extensions["errorCodes"] = collection.Flatten().Select(e => e.Code.Value).ToArray();

        return problemDetails;
    }

    /// <summary>
    /// Maps an <see cref="ErrorCategory"/> to its HTTP status code. The core names kinds of failure; this package
    /// owns their HTTP meaning (ADR-RESULT-001 D11).
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Category</term><description>Status</description></listheader>
    /// <item><term><see cref="ErrorCategory.NotFound"/></term><description>404 Not Found</description></item>
    /// <item><term><see cref="ErrorCategory.Validation"/>, <see cref="ErrorCategory.BusinessRule"/></term><description>422 Unprocessable Entity</description></item>
    /// <item><term><see cref="ErrorCategory.Unauthorized"/></term><description>401 Unauthorized</description></item>
    /// <item><term><see cref="ErrorCategory.Forbidden"/></term><description>403 Forbidden</description></item>
    /// <item><term><see cref="ErrorCategory.Conflict"/></term><description>409 Conflict</description></item>
    /// <item><term><see cref="ErrorCategory.TooManyRequests"/></term><description>429 Too Many Requests</description></item>
    /// <item><term><see cref="ErrorCategory.PreconditionFailed"/></term><description>412 Precondition Failed</description></item>
    /// <item><term><see cref="ErrorCategory.Timeout"/></term><description>408 Request Timeout</description></item>
    /// <item><term><see cref="ErrorCategory.Cancelled"/></term><description>499 Client Closed Request</description></item>
    /// <item><term><see cref="ErrorCategory.Technical"/>, and any undefined value</term><description>500 Internal Server Error</description></item>
    /// <item><term><see cref="ErrorCategory.NotSupported"/></term><description>501 Not Implemented</description></item>
    /// <item><term><see cref="ErrorCategory.External"/></term><description>502 Bad Gateway</description></item>
    /// <item><term><see cref="ErrorCategory.Unavailable"/></term><description>503 Service Unavailable</description></item>
    /// </list>
    /// </remarks>
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

    private static Dictionary<string, string[]>? GroupValidationErrors(IError error)
    {
        Dictionary<string, List<string>>? groups = null;
        Collect(error, ref groups);
        return groups?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.Ordinal);

        static void Collect(IError error, ref Dictionary<string, List<string>>? groups)
        {
            if (error is ErrorCollection collection)
            {
                foreach (var inner in collection)
                    Collect(inner, ref groups);
            }
            else if (error is IValidationError validation)
            {
                groups ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var propertyName = validation.PropertyName ?? string.Empty; // a third-party IValidationError may return null
                if (!groups.TryGetValue(propertyName, out var messages))
                    groups[propertyName] = messages = [];
                messages.Add(validation.Message);
            }
        }
    }

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
