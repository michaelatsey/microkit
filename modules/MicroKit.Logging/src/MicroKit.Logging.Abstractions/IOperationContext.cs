namespace MicroKit.Logging;

/// <summary>
/// Read-only ambient context carrying correlation and identity data for the current logical operation.
/// </summary>
/// <remarks>
/// <para>
/// Property access must be allocation-free and complete in ≤ 20 ns (backed by pre-allocated fields,
/// not lazy computation). Implementations must be <see langword="sealed"/>.
/// </para>
/// <para>
/// Obtain the current instance via <see cref="ILogContextAccessor.Current"/>.
/// Create a new scope via <see cref="ILogScopeFactory.BeginOperationScope()"/>.
/// </para>
/// </remarks>
public interface IOperationContext
{
    /// <summary>
    /// Cross-service correlation identifier. Never <see langword="null"/> once a scope is active.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// W3C TraceContext trace identifier (32 hex chars), or <see langword="null"/> if no Activity is active.
    /// </summary>
    string? TraceId { get; }

    /// <summary>
    /// W3C TraceContext span identifier (16 hex chars), or <see langword="null"/> if no Activity is active.
    /// </summary>
    string? SpanId { get; }

    /// <summary>
    /// HTTP request or message identifier, or <see langword="null"/> if not provided by the transport.
    /// </summary>
    string? RequestId { get; }

    /// <summary>
    /// Logical business operation identifier, or <see langword="null"/> if no operation scope has been started.
    /// </summary>
    string? OperationId { get; }

    /// <summary>
    /// Multi-tenant identifier, or <see langword="null"/> if the operation is not in a tenant context.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Authenticated user identifier, or <see langword="null"/> if the operation is anonymous.
    /// </summary>
    string? UserId { get; }
}
