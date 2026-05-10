using MicroKit.Security.Abstractions.Options;

namespace MicroKit.Security.Abstractions.Cache;

public interface ICacheableOptions
{
    CacheOptions Cache { get; }
}
