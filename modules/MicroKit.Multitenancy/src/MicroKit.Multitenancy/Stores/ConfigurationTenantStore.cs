namespace MicroKit.Multitenancy;

using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="ITenantStore"/> loaded from <see cref="MultitenancyOptions"/>.
/// </summary>
/// <remarks>
/// Register via <see cref="MultitenancyBuilder.UseConfigurationStore()"/> and bind the options:
/// <code>
/// services.Configure&lt;MultitenancyOptions&gt;(config.GetSection(MultitenancyOptions.SectionKey));
/// </code>
/// </remarks>
public sealed class ConfigurationTenantStore : ITenantStore
{
    private readonly IReadOnlyList<ITenantInfo> _tenants;

    /// <summary>
    /// Initializes a new <see cref="ConfigurationTenantStore"/> from the provided options.
    /// </summary>
    /// <param name="options">Multitenancy options containing the tenant list.</param>
    public ConfigurationTenantStore(IOptions<MultitenancyOptions> options)
        => _tenants = options.Value.Tenants.Cast<ITenantInfo>().ToList();

    /// <inheritdoc/>
    public ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default)
    {
        var tenant = _tenants.FirstOrDefault(t => t.Id == tenantId);
        return tenant is not null
            ? ValueTask.FromResult(Success(tenant))
            : ValueTask.FromResult(Failure<ITenantInfo>(MultitenancyErrors.TenantNotFound));
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default)
        => ValueTask.FromResult(_tenants);
}
