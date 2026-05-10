using MicroKit.Abstractions.Configuration;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.Messaging.Core.Contexts;
using MicroKit.Messaging.Core.Inbox;
using MicroKit.Messaging.Core.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace MicroKit.Messaging.Core.Extensions.Inbox;

public static class MicroKitInboxExtensions
{
    public static MicroKitMessagingBuilder UseInbox(
        this MicroKitMessagingBuilder builder,
        Action<InboxOptions>? configure = null,
        params Assembly[]? assembliesToScan)
    {
        var services = builder.Services;

        // Configuration des options
        services
            .AddOptions<InboxOptions>()
            .BindConfiguration("MicroKit:Messaging:Inbox") // Optionnel : permet de binder depuis appsettings.json
            .Configure(options => configure?.Invoke(options))
            .ValidateDataAnnotations() // Active les attributs [Range], [Required], etc.
            .ValidateOnStart();

        // 2. Détermination des assemblies à scanner
        var assemblies = assembliesToScan?.Length > 0
            ? assembliesToScan
            : [Assembly.GetCallingAssembly()];

        // 3. Enregistrement AUTOMATIQUE des Handlers (La partie demandée)
        var handlerTypes = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInboxHandler<>))
                .Select(i => new { Interface = i, Implementation = t }));

        foreach (var handler in handlerTypes)
        {
            // On enregistre chaque implémentation sous son interface spécifique (IInboxHandler<MonMessage>)
            services.TryAddScoped(handler.Interface, handler.Implementation);
        }

        services.AddMicroKitMessageContext();

        services.AddSingleton<IInboxConsumerRegistry>(new ReflectionInboxConsumerRegistry(assemblies));

        // --- SERVICES DE TRAITEMENT ---
        services.TryAddScoped<IInboxProcessor, DefaultInboxProcessor>();

        // --- WORKERS ---
        // Ils utilisent IOptions<InboxOptions> pour vérifier s'ils doivent s'exécuter
        services.AddHostedService<InboxPublisherWorker>();
        services.AddHostedService<InboxCleanupWorker>();
        
        return builder;
    }

    private static IServiceCollection AddMicroKitMessageContext(this IServiceCollection services)
    {
        // L'expert enregistre la même instance pour les deux interfaces dans le même Scope
        services.AddScoped<MicroKitMessageContext>();

        services.AddScoped<IMicroKitMessageContext>(sp =>
            sp.GetRequiredService<MicroKitMessageContext>());

        services.AddScoped<IMicroKitMessageContextSetter>(sp =>
            sp.GetRequiredService<MicroKitMessageContext>());

        return services;
    }
}
