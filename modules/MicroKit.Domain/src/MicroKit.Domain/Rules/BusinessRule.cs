namespace MicroKit.Domain.Rules;

/// <summary>
/// Abstract base class for business rules.
/// Provides common equality semantics and rule name extraction.
/// </summary>
public abstract class BusinessRule : IBusinessRule
{
    public abstract bool IsBroken();
    public abstract string Message { get; }

    public override bool Equals(object? obj)
    {
        if (obj is not BusinessRule other || GetType() != other.GetType())
            return false;

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }

    /// <summary>
    /// Override to provide the values that determine rule equality.
    /// Rules with same type and same parameters should be considered equal.
    /// </summary>
    protected virtual IEnumerable<object?> GetEqualityComponents()
    {
        yield return GetType();
    }
}