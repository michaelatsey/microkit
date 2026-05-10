using MicroKit.Abstractions.Configuration;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Extensions.Persistence;

/// <summary>
/// Réprésente les extensions de configuration pour la persistance dans le système de messagerie, fournissant des méthodes d'extension pour ajouter les référentiels de la boîte d'envoi (Outbox) et de la boîte de réception (Inbox) au conteneur de services, permettant ainsi une intégration facile et flexible des mécanismes de persistance pour la gestion des messages à envoyer et des messages entrants dans le système de messagerie.
/// Persistence – appelée par EF / Mongo packages
/// </summary>
public static class MicroKitPersistenceExtensions
{
    /// <summary>
    /// Adds the persistence.
    /// </summary>
    /// <typeparam name="TOutboxRepo">The type of the outbox repo.</typeparam>
    /// <typeparam name="TInboxRepo">The type of the inbox repo.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns></returns>
    public static MicroKitMessagingBuilder AddRepositories<TOutboxRepo, TInboxRepo>(
        this MicroKitMessagingBuilder builder)
        where TOutboxRepo : class, IOutboxRepository
        where TInboxRepo : class, IInboxMessageRepository
    {
        builder.Services.AddScoped<IOutboxRepository, TOutboxRepo>();
        builder.Services.AddScoped<IInboxMessageRepository, TInboxRepo>();

        return builder;
    }

}
