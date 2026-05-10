using MicroKit.Cqrs.Abstractions.Cache;

namespace MicroKit.Cqrs.Cache;

internal class DefaultCacheKeyService : ICacheKeyService
{
    public string BuildKey(string customKey) => customKey;
}
