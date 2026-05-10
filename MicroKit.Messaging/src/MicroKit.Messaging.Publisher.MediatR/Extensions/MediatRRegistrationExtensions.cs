using MicroKit.Abstractions.Configuration;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.Publisher.MediatR.Extensions;

public static class MediatRRegistrationExtensions
{
    public static MicroKitMessagingBuilder UseMediatRPublisher(this MicroKitMessagingBuilder builder)
    {
        builder.Services.AddScoped<IInboxPublisher, MediatRInboxPublisher>();
        builder.Services.AddScoped<IOutboxPublisher, MediatROutboxPublisher>();

        return builder;
    }
}
