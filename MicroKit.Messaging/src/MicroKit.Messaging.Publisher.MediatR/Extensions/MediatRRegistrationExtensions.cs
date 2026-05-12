using MicroKit.Abstractions.Configuration;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.Publisher.MediatR.Extensions;

/// <summary>Extension methods for registering the MediatR inbox and outbox publishers.</summary>
public static class MediatRRegistrationExtensions
{
    /// <summary>Registers <see cref="MediatRInboxPublisher"/> and <see cref="MediatROutboxPublisher"/> as the active publishers.</summary>
    /// <param name="builder">The messaging builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMessagingBuilder UseMediatRPublisher(this MicroKitMessagingBuilder builder)
    {
        builder.Services.AddScoped<IInboxPublisher, MediatRInboxPublisher>();
        builder.Services.AddScoped<IOutboxPublisher, MediatROutboxPublisher>();

        return builder;
    }
}
