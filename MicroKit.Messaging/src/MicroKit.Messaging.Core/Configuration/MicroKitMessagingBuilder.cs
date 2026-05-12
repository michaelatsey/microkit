using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.Core.Configuration;

/// <summary>
/// Réprésente le constructeur de configuration pour le système de messagerie, permettant de configurer les services liés à la boîte d'envoi (Outbox), à la boîte de réception (Inbox), à la sérialisation des messages et au transport des messages, en utilisant une approche fluide pour faciliter la configuration et l'extension du système de messagerie selon les besoins spécifiques de l'application.
/// </summary>
public class MicroKitMessagingBuilder
{
    /// <summary>Gets the underlying service collection.</summary>
    public IServiceCollection Services { get; }
    private readonly MessagingOptions _options;
    /// <summary>Initializes a new instance.</summary>
    /// <param name="services">The service collection to configure.</param>
    public MicroKitMessagingBuilder(IServiceCollection services)
    {
        Services = services;
        _options = new MessagingOptions();
    }

    /// <summary>Applies additional <see cref="MessagingOptions"/> configuration.</summary>
    /// <param name="configure">Configuration delegate.</param>
    public void Configure(Action<MessagingOptions> configure)
    {
        configure(_options);
    }
}


