using MicroKit.Messaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Extensions.Transport;

internal static class MicroKitTransportRegistration
{
    internal static void AddTransport<T>(
        IServiceCollection services)
        where T : class, IMessageTransport
    {
        services.AddSingleton<IMessageTransport, T>();
    }
}
