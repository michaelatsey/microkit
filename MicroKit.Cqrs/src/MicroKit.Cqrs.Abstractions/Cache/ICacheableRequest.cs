using MicroKit.Caching;

namespace MicroKit.Cqrs.Abstractions.Cache;


// Version plus avancée avec plus de contrôle
public interface ICacheableRequest 
{
    string CacheKey { get; }
    TimeSpan? CacheDuration { get; }
    CacheOptions Options { get; }
}
