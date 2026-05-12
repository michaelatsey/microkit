using MicroKit.Abstractions.Configuration;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.Messaging.Core.Internal.Validation;
using MicroKit.Messaging.Core.Internal.Validation.Outbox;
using MicroKit.Messaging.Core.Outbox;
using Microsoft.Extensions.Configuration; // Ajouté pour BindConfiguration
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Extensions.Outbox;

/// <summary>Extension methods for enabling the Outbox pattern on a <see cref="MicroKitMessagingBuilder"/>.</summary>
public static class MicroKitOutboxExtensions
{
    /// <summary>Enables outbox processing and registers the publisher and cleanup background workers.</summary>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="configure">Optional outbox configuration delegate.</param>
    public static MicroKitMessagingBuilder UseOutbox(
        this MicroKitMessagingBuilder builder,
        Action<OutboxOptions>? configure = null)
    {
        var services = builder.Services;

        services
            .AddOptions<OutboxOptions>()
            .BindConfiguration("MicroKit:Messaging:Outbox") // Optionnel : permet de binder depuis appsettings.json
            .Configure(options => configure?.Invoke(options))
            .ValidateDataAnnotations() // Active les attributs [Range], [Required], etc.
            .ValidateOnStart();

        //  Enregistre le validateur pour les TimeSpan
        builder.Services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();

        // Enregistrement du validateur de module pour les dépendances (IOutboxPublisher)
        builder.Services.AddSingleton<IMessagingModuleValidator, OutboxModuleValidator>();
        //if (configure is not null)
        //    services.Configure(configure);

        services.TryAddScoped<IOutboxProcessor, DefaultOutboxProcessor>();
        services.AddScoped<IOutboxService, OutboxService>();

        // Worker toujours enregistré, activation contrôlée par options
        services.AddHostedService<OutboxPublisherWorker>();
        services.AddHostedService<OutboxCleanupWorker>();

        return builder;
    }
}
