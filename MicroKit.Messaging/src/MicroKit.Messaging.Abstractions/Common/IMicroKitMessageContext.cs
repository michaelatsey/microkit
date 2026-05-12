namespace MicroKit.Messaging.Abstractions.Common;

/// <summary>
/// Provides read access to the ambient messaging context for the current outbox/inbox flow,
/// including tenant, correlation, causation, and idempotency metadata.
/// </summary>
public interface IMicroKitMessageContext
{
    /// <summary>Gets the tenant identifier for the current messaging operation.</summary>
    string TenantId { get; }

    /// <summary>Gets the optional correlation identifier used for distributed tracing.</summary>
    string? CorrelationId { get; }

    /// <summary>Gets the optional idempotency key for the current operation.</summary>
    string? IdempotencyKey { get; }

    /// <summary>Gets the optional causation identifier linking this message to its cause.</summary>
    string? CausationId { get; }

    /// <summary>Gets a value indicating whether the current flow is running inside an outbox dispatch.</summary>
    bool IsInProcess { get; }
}

/// <summary>Allows infrastructure code to push a messaging context onto the ambient scope.</summary>
public interface IMicroKitMessageContextSetter
{
    /// <summary>
    /// Establishes an ambient messaging context for the duration of the returned scope.
    /// Disposing the returned <see cref="IDisposable"/> clears the context.
    /// </summary>
    /// <param name="tenantId">The tenant identifier for this scope.</param>
    /// <param name="correlationId">Optional correlation identifier for distributed tracing.</param>
    /// <param name="causationId">Optional causation identifier.</param>
    /// <param name="idempotencyKey">Optional idempotency key.</param>
    /// <returns>A disposable that removes the context when disposed.</returns>
    IDisposable SetContext(
        string tenantId,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null
        );
}
