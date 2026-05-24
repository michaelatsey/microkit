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

    /// <summary>An external dependency (API, service, provider) returned a failure (HTTP 502).</summary>
    External,

    /// <summary>A domain business rule was violated (HTTP 422).</summary>
    BusinessRule,

    /// <summary>The requested operation is not supported (HTTP 501).</summary>
    NotSupported,

    /// <summary>The operation exceeded its time limit (HTTP 408).</summary>
    Timeout,

    /// <summary>The operation was cancelled by the caller (HTTP 499).</summary>
    Cancelled,

    /// <summary>A required precondition was not met (HTTP 412).</summary>
    PreconditionFailed,
}
