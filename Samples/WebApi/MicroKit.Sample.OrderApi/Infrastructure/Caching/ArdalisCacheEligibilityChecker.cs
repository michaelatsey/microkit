using MicroKit.Cqrs.Abstractions.Cache;

namespace MicroKit.Sample.OrderApi.Infrastructure.Caching;

/// <summary>Cache eligibility checker that integrates with Ardalis Result types.</summary>
public class ArdalisCacheEligibilityChecker : ICacheEligibilityChecker
{
    /// <inheritdoc/>
    public bool IsEligible(object? result)
    {
        if (result is null) return false;

        // On lie Ardalis ici, et seulement ici !
        if (result is Ardalis.Result.IResult r)
        {
            return r.Status is Ardalis.Result.ResultStatus.Ok;
        }
        return true;
    }
}
