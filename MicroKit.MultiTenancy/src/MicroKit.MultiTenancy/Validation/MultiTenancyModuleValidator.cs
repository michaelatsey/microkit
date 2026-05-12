using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Validation;

/// <summary>Validates that required multi-tenancy services (e.g. <see cref="ITenantRegistry"/>) are registered at application startup.</summary>
public class MultiTenancyModuleValidator : IModuleValidator
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly MicroKitMultiTenancyOptions _multiTenancyOptions;
    /// <summary>Initializes a new instance.</summary>
    /// <param name="serviceScopeFactory">Factory for resolving scoped services during validation.</param>
    /// <param name="outboxOptions">Multi-tenancy configuration options.</param>
    public MultiTenancyModuleValidator(IServiceScopeFactory serviceScopeFactory, IOptions<MicroKitMultiTenancyOptions> outboxOptions)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _multiTenancyOptions = outboxOptions.Value;
    }

    /// <inheritdoc/>
    public void Validate()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<ITenantRegistry>() ?? throw new InvalidOperationException(
                   "MultiTenancy Error: You use MultiTenancy but no ITenantRegistry is registered. " +
                   "Please provide a custom implementation");
    }
}
