namespace MicroKit.Cqrs.Abstractions.Cache;

/// <summary>Marks a command as a cache invalidator — returns the keys to remove after successful execution.</summary>
public interface ICacheInvalidatorRequest<TRequest, TResponse>
{
    IEnumerable<string> GetCacheKeys(TRequest request, TResponse response);
}
