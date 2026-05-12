namespace MicroKit.Cqrs.Abstractions.Cache;

/// <summary>Marks a command as a cache invalidator — returns the keys to remove after successful execution.</summary>
public interface ICacheInvalidatorRequest<TRequest, TResponse>
{
    /// <summary>Returns the cache keys to invalidate after successful command execution.</summary>
    /// <param name="request">The command that was executed.</param>
    /// <param name="response">The result produced by the command.</param>
    /// <returns>The cache keys to remove.</returns>
    IEnumerable<string> GetCacheKeys(TRequest request, TResponse response);
}
