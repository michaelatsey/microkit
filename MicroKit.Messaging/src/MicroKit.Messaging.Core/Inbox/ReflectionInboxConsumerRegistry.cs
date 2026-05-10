using MicroKit.Messaging.Abstractions.Inbox;
using System.Reflection;

namespace MicroKit.Messaging.Core.Inbox;

public class ReflectionInboxConsumerRegistry : IInboxConsumerRegistry
{
    private readonly List<string> _consumerNames;

    public ReflectionInboxConsumerRegistry(IEnumerable<Assembly> assembliesToScan)
    {
        // On scanne les assemblies pour trouver les implémentations de IInboxHandler<>
        _consumerNames = [.. assembliesToScan
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces())
            // On cherche ITenantInboxHandler<T> ou IInboxHandler<T> selon ton nommage
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition().Name.Contains("IInboxHandler"))
            // On peut utiliser le nom de la classe ou un attribut [ConsumerName]
            .Select(i => GetConsumerNameFromType(i.GetGenericArguments()[0]))
            .Distinct()];
    }

    private static string GetConsumerNameFromType(Type messageType)
    {
        // Par défaut, on utilise le nom du type du message traité
        // Mais on pourrait lire un attribut personnalisé ici
        return messageType.Name.ToLowerInvariant();
    }

    public IReadOnlyList<string> GetConsumerNames() => _consumerNames;
}