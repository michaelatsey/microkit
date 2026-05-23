namespace MicroKit.Domain.Rules;

/// <summary>
/// Abstract base class for business rules.
/// Provides common equality semantics and rule name extraction.
/// </summary>
public abstract class BusinessRule : IBusinessRule
{
    /// <summary>
    /// Determines whether this business rule is currently broken.
    /// </summary>
    /// <returns>True if the rule is violated; otherwise, false</returns>
    public abstract bool IsBroken();

    /// <summary>
    /// Gets the message that explains why this rule was broken.
    /// </summary>
    public abstract string Message { get; }

    /// <summary>
    /// Determines whether the specified object is equal to the current business rule.
    /// Business rules are equal if they have the same type and same equality components.
    /// </summary>
    /// <param name="obj">The object to compare with the current business rule</param>
    /// <returns>True if the specified object is equal to the current business rule; otherwise, false</returns>
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

    /// <summary>
    /// Returns a hash code for this business rule based on its equality components.
    /// </summary>
    /// <returns>A hash code for the current business rule</returns>
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