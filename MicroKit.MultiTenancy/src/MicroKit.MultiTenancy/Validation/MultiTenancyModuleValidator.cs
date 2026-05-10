using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Validation;

public class MultiTenancyModuleValidator : IModuleValidator
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly MicroKitMultiTenancyOptions _multiTenancyOptions;
    public MultiTenancyModuleValidator(IServiceScopeFactory serviceScopeFactory, IOptions<MicroKitMultiTenancyOptions> outboxOptions)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _multiTenancyOptions = outboxOptions.Value;
    }

    public void Validate()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<ITenantRegistry>() ?? throw new InvalidOperationException(
                   "MultiTenancy Error: You use MultiTenancy but no ITenantRegistry is registered. " +
                   "Please provide a custom implementation");
    }
}
