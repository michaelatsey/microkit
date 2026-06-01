namespace MicroKit.MediatR;

/// <summary>
/// Canonical pipeline order registry for MicroKit.MediatR behaviors.
/// Values are a contract — changing any existing value is a breaking change.
/// </summary>
/// <remarks>
/// Custom behaviors should use values in the ranges 101–199, 201–299, etc. to
/// interleave with built-ins, or 601–999 to run after <c>RetryBehavior</c>.
/// Values below 100 or above 999 require written justification.
/// </remarks>
public static class PipelineOrder
{
    /// <summary>
    /// <c>LoggingBehavior</c> — always active, never opt-in. Observes every request and response.
    /// Must be first so authorization and validation failures are also recorded.
    /// </summary>
    public const int Logging = 100;

    /// <summary>
    /// <c>AuthorizationBehavior</c> — opt-in via <see cref="IAuthorizedRequest"/>.
    /// Fails fast before validation to avoid leaking information in error messages.
    /// </summary>
    public const int Authorization = 200;

    /// <summary>
    /// <c>ValidationBehavior</c> — opt-in via a registered <c>IValidator&lt;TRequest&gt;</c>.
    /// Rejects malformed input before any business behavior runs.
    /// </summary>
    public const int Validation = 300;

    /// <summary>
    /// <c>IdempotencyBehavior</c> — opt-in via <see cref="IIdempotentCommand"/> (commands only).
    /// De-duplicates commands before they reach the handler.
    /// </summary>
    public const int Idempotency = 400;

    /// <summary>
    /// <c>CachingBehavior</c> — opt-in via <see cref="ICacheableQuery"/> (queries only).
    /// Serves results from cache before the handler executes.
    /// </summary>
    public const int Caching = 500;

    /// <summary>
    /// <c>RetryBehavior</c> — opt-in via <see cref="IRetryableRequest"/>.
    /// Wraps the handler call in a Polly exponential back-off retry pipeline.
    /// </summary>
    public const int Retry = 600;
}
