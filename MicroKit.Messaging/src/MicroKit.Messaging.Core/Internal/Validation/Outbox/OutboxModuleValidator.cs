using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Internal.Validation.Outbox;

internal class OutboxModuleValidator : IMessagingModuleValidator
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly OutboxOptions _outboxOptions;
    public OutboxModuleValidator(IServiceScopeFactory serviceScopeFactory, IOptions<OutboxOptions> outboxOptions)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _outboxOptions = outboxOptions.Value;
    }

    public void Validate()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        if (_outboxOptions.Enabled)
        {
            _ = scope.ServiceProvider.GetService<IOutboxPublisher>() ?? throw new InvalidOperationException(
                    "Messaging Error: Outbox is enabled but no IOutboxPublisher is registered. " +
                    "Please call .UseMediatRPublisher() or provide a custom implementation.");
        }
        _ = scope.ServiceProvider.GetService<ITenantRegistry>() ?? throw new InvalidOperationException(
                   "MultiTenancy Error: You use MultiTenancy but no ITenantRegistry is registered. " +
                   "Please provide a custom implementation");

    }
}
