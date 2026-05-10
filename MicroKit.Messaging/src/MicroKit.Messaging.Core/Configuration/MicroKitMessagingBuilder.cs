using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.Core.Configuration;

/// <summary>
/// Réprésente le constructeur de configuration pour le système de messagerie, permettant de configurer les services liés à la boîte d'envoi (Outbox), à la boîte de réception (Inbox), à la sérialisation des messages et au transport des messages, en utilisant une approche fluide pour faciliter la configuration et l'extension du système de messagerie selon les besoins spécifiques de l'application.
/// </summary>
public class MicroKitMessagingBuilder
{
    public IServiceCollection Services { get; }
    private readonly MessagingOptions _options;
    public MicroKitMessagingBuilder(IServiceCollection services)
    {
        Services = services;
        _options = new MessagingOptions();
    }

    public void Configure(Action<MessagingOptions> configure)
    {
        configure(_options);
    }
}


