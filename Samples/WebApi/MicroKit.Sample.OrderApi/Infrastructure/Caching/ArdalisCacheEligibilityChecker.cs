using MicroKit.Cqrs.Abstractions.Cache;

namespace MicroKit.Sample.OrderApi.Infrastructure.Caching;

public class ArdalisCacheEligibilityChecker : ICacheEligibilityChecker
{
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
