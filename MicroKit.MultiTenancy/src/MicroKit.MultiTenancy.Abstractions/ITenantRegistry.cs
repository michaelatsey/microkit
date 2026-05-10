using System.Collections.ObjectModel;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantRegistry
{
    Task<ReadOnlyCollection<string>> GetAllTenantsAsync(CancellationToken cancellationToken = default);
}
