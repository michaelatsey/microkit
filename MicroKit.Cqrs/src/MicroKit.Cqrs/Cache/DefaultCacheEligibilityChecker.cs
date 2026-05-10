using MicroKit.Cqrs.Abstractions.Cache;

namespace MicroKit.Cqrs.Cache;

public class DefaultCacheEligibilityChecker : ICacheEligibilityChecker
{
    public bool IsEligible(object? result) => result is not null;
}
