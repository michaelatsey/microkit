namespace MicroKit.Domain.Specifications;

/// <summary>
/// Abstract base implementation of ISpecification with composition operations.
/// Provides And, Or, Not operations for combining specifications.
/// </summary>
/// <typeparam name="T">The type this specification applies to</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    /// <summary>
    /// Determines whether the given candidate satisfies this specification.
    /// </summary>
    /// <param name="candidate">The candidate to evaluate</param>
    /// <returns>True if the candidate satisfies this specification; otherwise, false</returns>
    public abstract bool IsSatisfiedBy(T candidate);

    /// <summary>
    /// Converts this specification to a LINQ expression that can be used with query providers.
    /// </summary>
    /// <returns>An expression representing this specification's logic</returns>
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <summary>
    /// Creates a specification that is satisfied when both this and the other are satisfied.
    /// </summary>
    public ISpecification<T> And(ISpecification<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new AndSpecification<T>(this, other);
    }

    /// <summary>
    /// Creates a specification that is satisfied when either this or the other is satisfied.
    /// </summary>
    public ISpecification<T> Or(ISpecification<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new OrSpecification<T>(this, other);
    }

    /// <summary>
    /// Creates a specification that is satisfied when this specification is not satisfied.
    /// </summary>
    public ISpecification<T> Not()
    {
        return new NotSpecification<T>(this);
    }

    /// <summary>
    /// Implicit conversion to expression for convenient LINQ usage.
    /// </summary>
    public static implicit operator Expression<Func<T, bool>>(Specification<T> specification)
    {
        return specification.ToExpression();
    }
}