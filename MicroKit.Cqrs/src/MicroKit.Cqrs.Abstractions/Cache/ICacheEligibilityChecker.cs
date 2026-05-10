namespace MicroKit.Cqrs.Abstractions.Cache;

public interface ICacheEligibilityChecker
{
    bool IsEligible(object? result);
}
