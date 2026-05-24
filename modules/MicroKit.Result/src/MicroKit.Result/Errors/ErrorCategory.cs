namespace MicroKit.Result;

/// <summary>
/// Categorizes errors for HTTP status code mapping and error handling strategies.
/// </summary>
public enum ErrorCategory
{
    /// <summary>The requested resource was not found (HTTP 404).</summary>
    NotFound,

    /// <summary>Input validation failed (HTTP 422).</summary>
    Validation,

    /// <summary>Authentication is required (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The authenticated user lacks permission (HTTP 403).</summary>
    Forbidden,

    /// <summary>A resource conflict occurred (HTTP 409).</summary>
    Conflict,

    /// <summary>Rate limit exceeded (HTTP 429).</summary>
    TooManyRequests,

    /// <summary>An internal technical error occurred (HTTP 500).</summary>
    Technical,

    /// <summary>The service is temporarily unavailable (HTTP 503).</summary>
    Unavailable,
}
