using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace MicroKit.Messaging.Core.Internal.Validation;

internal class MessagingValidationService(IEnumerable<IMessagingModuleValidator> validators) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            validator.Validate();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
