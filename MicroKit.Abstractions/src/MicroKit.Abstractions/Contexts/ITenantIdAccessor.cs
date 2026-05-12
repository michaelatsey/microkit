namespace MicroKit.Abstractions.Contexts;

/// <summary>Provides read access to the current tenant identifier for the executing context.</summary>
public interface ITenantIdAccessor
{
    /// <summary>Gets the current tenant identifier, or <see langword="null"/> if no tenant is active.</summary>
    string? TenantId { get; }
}
