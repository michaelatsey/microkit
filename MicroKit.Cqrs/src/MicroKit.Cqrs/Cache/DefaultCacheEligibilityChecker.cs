using MicroKit.Cqrs.Abstractions.Cache;

namespace MicroKit.Cqrs.Cache;

/// <summary>Default <see cref="ICacheEligibilityChecker"/> that considers any non-null result eligible for caching.</summary>
public class DefaultCacheEligibilityChecker : ICacheEligibilityChecker
{
    /// <inheritdoc/>
    public bool IsEligible(object? result) => result is not null;
}
