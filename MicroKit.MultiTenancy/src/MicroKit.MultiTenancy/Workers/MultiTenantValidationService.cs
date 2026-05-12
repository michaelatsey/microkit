using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Workers;

/// <summary>Hosted service that runs all registered <see cref="IModuleValidator"/> instances at application startup.</summary>
public class MultiTenantValidationService(
    IEnumerable<IModuleValidator> validators,
    IOptions<MicroKitMultiTenancyOptions> options) : IHostedService
{
    private readonly IEnumerable<IModuleValidator> _validators = validators;
    private readonly MicroKitMultiTenancyOptions _options = options.Value;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableValidationWorker)
            return Task.CompletedTask;

        foreach (var validator in _validators)
        {
            validator.Validate();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
