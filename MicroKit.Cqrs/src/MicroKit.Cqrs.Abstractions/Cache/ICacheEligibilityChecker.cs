namespace MicroKit.Cqrs.Abstractions.Cache;

/// <summary>Determines whether a query result is eligible to be stored in cache.</summary>
public interface ICacheEligibilityChecker
{
    /// <summary>Returns <see langword="true"/> if the given result should be cached.</summary>
    /// <param name="result">The query result to evaluate.</param>
    bool IsEligible(object? result);
}
