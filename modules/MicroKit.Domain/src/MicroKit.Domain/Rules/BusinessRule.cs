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

        var left = GetEqualityComponents();
        var right = other.GetEqualityComponents();

        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var components = GetEqualityComponents();

        for (var i = 0; i < components.Length; i++)
        {
            hash.Add(components[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Override to provide the values that determine rule equality.
    /// Rules with same type and same parameters should be considered equal.
    /// Returns an array for optimal performance with minimal allocations.
    /// </summary>
    /// <example>
    /// protected override object?[] GetEqualityComponents() =>
    ///     [MinAmount, MaxAmount, Currency];
    /// </example>
    protected virtual object?[] GetEqualityComponents() => [GetType()];
}