namespace MicroKit.Result;

/// <summary>
/// Facts about <see cref="ErrorCategory"/> values that the core needs without knowing any transport.
/// </summary>
internal static class ErrorCategoryFacts
{
    /// <summary>
    /// Returns <see langword="true"/> for a category that reports a fault of the system rather than of the
    /// request: <see cref="ErrorCategory.Technical"/>, <see cref="ErrorCategory.Unavailable"/>,
    /// <see cref="ErrorCategory.External"/>, <see cref="ErrorCategory.NotSupported"/>, <see cref="ErrorCategory.Timeout"/>, and any value the enum
    /// does not define. <c>MicroKit.Result.AspNetCore</c> maps exactly these to a 5xx status; an integration
    /// test keeps the two in step.
    /// </summary>
    internal static bool IsServerFault(ErrorCategory category) => category switch
    {
        ErrorCategory.Technical or ErrorCategory.Unavailable or ErrorCategory.External or ErrorCategory.NotSupported
            or ErrorCategory.Timeout => true,
        ErrorCategory.NotFound or ErrorCategory.Validation or ErrorCategory.Unauthorized or ErrorCategory.Forbidden
            or ErrorCategory.Conflict or ErrorCategory.TooManyRequests or ErrorCategory.BusinessRule
            or ErrorCategory.Cancelled or ErrorCategory.PreconditionFailed => false,
        _ => true,
    };
}
