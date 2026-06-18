namespace MicroKit.Execution.Abstractions;

/// <summary>
/// A generic, extensible execution context passed to
/// <see cref="IExecutionScopeFactory.CreateScopeAsync"/> so that scope implementations
/// can hydrate tenant, correlation, and custom properties without coupling callers to
/// Multitenancy or any other concrete concern.
/// </summary>
/// <remarks>
/// All properties are <see langword="string?"/> by design — this is a Level 0 contract
/// with a single dependency on <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>.
/// Typed value objects (e.g. <c>CorrelationId</c>, <c>CausationId</c> from
/// <c>MicroKit.Messaging.Abstractions</c>) must NOT leak into this interface.
/// Callers convert to <see langword="string"/> before populating the context.
/// </remarks>
public interface IExecutionContext
{
    /// <summary>
    /// The tenant identifier for this execution scope, or <see langword="null"/> for
    /// single-tenant / tenant-agnostic contexts.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// The correlation identifier that links this unit of work to a chain of related
    /// operations, or <see langword="null"/> when no upstream correlation context exists.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// The causation identifier of the event or command that triggered this unit of
    /// work, or <see langword="null"/> for root operations.
    /// </summary>
    string? CausationId { get; }

    /// <summary>
    /// An extensible property bag for additional context values (shard, region,
    /// environment, etc.) that are not represented by first-class properties.
    /// Implementations should return an empty dictionary when no extra properties exist.
    /// </summary>
    IReadOnlyDictionary<string, object?> Properties { get; }
}
