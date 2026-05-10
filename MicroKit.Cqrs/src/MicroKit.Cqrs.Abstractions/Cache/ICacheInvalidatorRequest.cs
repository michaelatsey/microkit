namespace MicroKit.Cqrs.Abstractions.Cache;

public interface ICacheInvalidatorRequest<in TRequest, in TResponse>
{
    // Retourne les clés qu'il faut supprimer après le succès de la commande
    IEnumerable<string> GetCacheKeys(TRequest request, TResponse response);
}
