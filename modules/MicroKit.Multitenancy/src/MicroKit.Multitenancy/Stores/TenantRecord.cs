namespace MicroKit.Multitenancy;

/// <summary>
/// Default concrete implementation of <see cref="ITenantInfo"/>.
/// Used by <see cref="InMemoryTenantStore"/>, <see cref="ConfigurationTenantStore"/>,
/// and consumer test fixtures.
/// </summary>
public sealed record TenantRecord : ITenantInfo
{
    /// <inheritdoc/>
    public required TenantId Id { get; init; }

    /// <inheritdoc/>
    public required string Name { get; init; }

    /// <inheritdoc/>
    public string? ConnectionString { get; init; }

    /// <inheritdoc/>
    public string? SchemaName { get; init; }

    /// <inheritdoc/>
    public bool IsActive { get; init; } = true;
}
