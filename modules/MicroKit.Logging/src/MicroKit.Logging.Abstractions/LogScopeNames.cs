namespace MicroKit.Logging;

/// <summary>
/// Canonical scope names used when creating structured log scopes via <see cref="ILogScopeFactory"/>.
/// Use these constants as scope keys to ensure consistent scope naming across the ecosystem.
/// </summary>
public static class LogScopeNames
{
    /// <summary>Scope name for a logical operation boundary (correlation, operation ID, tenant, user).</summary>
    public const string Operation = "MicroKit.Operation";

    /// <summary>Scope name for a correlation-only boundary (correlation ID propagation only).</summary>
    public const string Correlation = "MicroKit.Correlation";

    /// <summary>Scope name for a tenant boundary (tenant context activation).</summary>
    public const string Tenant = "MicroKit.Tenant";
}
