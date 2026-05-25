namespace MicroKit.Logging;

/// <summary>
/// Canonical structured log property names shared across all MicroKit modules.
/// Always use these constants — never hardcode property name strings.
/// Enforced at compile time by the <c>MKL002x</c> analyzer family.
/// </summary>
public static class LogPropertyNames
{
    /// <summary>Cross-service correlation identifier. Propagated via <c>X-Correlation-ID</c> header and Activity baggage.</summary>
    public const string CorrelationId = "CorrelationId";

    /// <summary>W3C TraceContext trace identifier (32 hex chars). Extracted from <c>Activity.Current.TraceId</c>.</summary>
    public const string TraceId = "TraceId";

    /// <summary>W3C TraceContext span identifier (16 hex chars). Extracted from <c>Activity.Current.SpanId</c>.</summary>
    public const string SpanId = "SpanId";

    /// <summary>
    /// Unique identifier for an individual HTTP request or message.
    /// Distinct from <see cref="CorrelationId"/> — does not propagate to downstream services.
    /// </summary>
    public const string RequestId = "RequestId";

    /// <summary>Identifier for a logical business operation scope (e.g., a CQRS command execution).</summary>
    public const string OperationId = "OperationId";

    /// <summary>Multi-tenant identifier. Set by <c>MicroKit.Multitenancy</c>.</summary>
    public const string TenantId = "TenantId";

    /// <summary>Authenticated user identifier. Set by <c>MicroKit.Auth</c>.</summary>
    public const string UserId = "UserId";

    /// <summary>Name of the CQRS command being executed. Set by <c>MicroKit.MediatR</c>.</summary>
    public const string CommandName = "CommandName";

    /// <summary>Unique identifier for a message in <c>MicroKit.Messaging</c> (outbox, broker).</summary>
    public const string MessageId = "MessageId";
}
