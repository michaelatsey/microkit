namespace MicroKit.Result;

/// <summary>
/// The kind of failure an error reports. It is the only input to a transport's status: the HTTP mapping lives in
/// <c>MicroKit.Result.AspNetCore</c> (<c>ResultProblemDetailsFactory.ToStatusCode</c>), not in the core.
/// </summary>
public enum ErrorCategory
{
    /// <summary>The requested resource does not exist.</summary>
    NotFound,

    /// <summary>The input is invalid.</summary>
    Validation,

    /// <summary>The caller is not authenticated.</summary>
    Unauthorized,

    /// <summary>The authenticated caller may not perform the operation.</summary>
    Forbidden,

    /// <summary>The operation conflicts with the current state of a resource.</summary>
    Conflict,

    /// <summary>The caller exceeded a rate limit.</summary>
    TooManyRequests,

    /// <summary>An internal fault of the system. The default category of <see cref="Error"/>.</summary>
    Technical,

    /// <summary>A service the operation needs is temporarily unavailable.</summary>
    Unavailable,

    /// <summary>An external dependency (API, service, provider) returned a failure.</summary>
    External,

    /// <summary>A business rule of the domain forbids the operation.</summary>
    BusinessRule,

    /// <summary>The requested operation is not supported.</summary>
    NotSupported,

    /// <summary>The operation exceeded its time limit.</summary>
    Timeout,

    /// <summary>
    /// The operation was cancelled by something other than the caller's own cancellation, such as a downstream
    /// service. A cancellation the caller requested propagates as <see cref="OperationCanceledException"/>; it
    /// never becomes an error.
    /// </summary>
    Cancelled,

    /// <summary>A precondition of the operation was not met.</summary>
    PreconditionFailed,
}
