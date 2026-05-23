namespace MicroKit.Domain.Specifications;

/// <summary>
/// Represents a business rule that can be checked and converted to expressions.
/// Used for filtering, querying, and business rule validation.
/// </summary>
/// <typeparam name="T">The type this specification applies to</typeparam>
public interface ISpecification<T>
{
    /// <summary>
    /// Checks if the given candidate satisfies this specification.
    /// </summary>
    /// <param name="candidate">The object to test</param>
    /// <returns>True if the specification is satisfied</returns>
    bool IsSatisfiedBy(T candidate);

    /// <summary>
    /// Converts this specification to a LINQ expression.
    /// Enables use with Entity Framework and other query providers.
    /// </summary>
    /// <returns>Expression that represents this specification</returns>
    Expression<Func<T, bool>> ToExpression();
}