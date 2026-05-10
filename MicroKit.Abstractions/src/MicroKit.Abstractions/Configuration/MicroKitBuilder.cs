using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Abstractions.Configuration;

/// <summary>
/// Réprésente le constructeur de configuration pour le système de messagerie, permettant de configurer les services liés à la boîte d'envoi (Outbox), à la boîte de réception (Inbox), à la sérialisation des messages et au transport des messages, en utilisant une approche fluide pour faciliter la configuration et l'extension du système de messagerie selon les besoins spécifiques de l'application.
/// </summary>
public class MicroKitBuilder
{
    public IServiceCollection Services { get; }
    private readonly MicroKitOptions _options;

    public MicroKitBuilder(IServiceCollection services)
    {
        Services = services;
        _options = new MicroKitOptions();

    }

    public MicroKitBuilder Configure(Action<MicroKitOptions>? configure = null)
    {
        configure?.Invoke(_options);
        return this;
    }
}


