using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Workers;

public class MultiTenantValidationService(
    IEnumerable<IModuleValidator> validators,
    IOptions<MicroKitMultiTenancyOptions> options) : IHostedService
{
    private readonly IEnumerable<IModuleValidator> _validators = validators;
    private readonly MicroKitMultiTenancyOptions _options = options.Value;

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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
