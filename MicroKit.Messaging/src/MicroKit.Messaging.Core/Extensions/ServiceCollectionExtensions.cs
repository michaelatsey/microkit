using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using MicroKit.Core.Serialization;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.Messaging.Core.Internal.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static MicroKitBuilder AddMicroKitMessaging(this MicroKitBuilder builder, Action<MicroKitMessagingBuilder>? configure )
    {
        var services = builder.Services;

        services
            .AddOptions<MessagingOptions>()
            .ValidateOnStart();

        var messagingBuilder = new MicroKitMessagingBuilder(services);
        configure?.Invoke(messagingBuilder);
        
        // ENREGISTREMENT DU SERVICE DE VALIDATION
        // On l'enregistre en tant que IHostedService pour qu'il démarre avec l'app
        services.AddHostedService<MessagingValidationService>();

        // Ajout des services de base pour l'inbox et l'outbox
        // Désormais ajouter dépuis l'extension spécifique 
        return builder;
    }
}