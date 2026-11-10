namespace MicroKit.Messaging.Spi;

/// <summary>
/// The reserved header names of <see cref="MessageEnvelope.Headers"/>: the CloudEvents extension
/// attributes MicroKit writes on every message it sends.
/// </summary>
/// <remarks>
/// The bus sets these after the send middleware runs and overwrites anything a middleware or a
/// caller set. Names are lower-case alphanumeric, as CloudEvents requires of extension attributes.
/// </remarks>
public static class MessageHeaders
{
    /// <summary>The tenant the message belongs to. Absent in a single-tenant deployment.</summary>
    public const string TenantId = "tenantid";

    /// <summary>The correlation identifier of the chain the message belongs to.</summary>
    public const string CorrelationId = "correlationid";

    /// <summary>The identifier of the message that caused this one. Absent on a root.</summary>
    public const string CausationId = "causationid";

    /// <summary>The W3C trace context of the publication, when a trace was active.</summary>
    public const string TraceParent = "traceparent";

    /// <summary>Returns whether <paramref name="name"/> is one of the reserved header names.</summary>
    /// <param name="name">The header name. Compared ignoring case.</param>
    /// <returns><see langword="true"/> for a reserved name.</returns>
    /// <remarks>
    /// Case-insensitive on purpose: several broker bindings carry headers case-insensitively, so a
    /// <c>TenantId</c> next to the bus's <c>tenantid</c> would be two values for one attribute — a
    /// way around the rule that a tenant cannot be forged.
    /// </remarks>
    public static bool IsReserved(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Equals(name, TenantId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, CorrelationId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, CausationId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, TraceParent, StringComparison.OrdinalIgnoreCase);
    }
}
