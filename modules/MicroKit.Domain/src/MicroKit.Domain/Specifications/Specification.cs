namespace MicroKit.Domain.Specifications;

/// <summary>
/// Abstract base implementation of ISpecification with composition operations.
/// Provides And, Or, Not operations for combining specifications.
/// </summary>
/// <typeparam name="T">The type this specification applies to</typeparam>
/// <remarks>
/// A derived specification implements <see cref="ToExpression"/> only. <see cref="IsSatisfiedBy"/>
/// evaluates that same expression in memory, so a query provider translating the expression and an
/// in-memory evaluation can never disagree.
/// </remarks>
public abstract class Specification<T> : ISpecification<T>
{
    private Func<T, bool>? _predicate;

    /// <summary>
    /// Determines whether the given candidate satisfies this specification.
    /// </summary>
    /// <param name="candidate">The candidate to evaluate</param>
    /// <returns>True if the candidate satisfies this specification; otherwise, false</returns>
    /// <remarks>
    /// <para>
    /// Evaluates <see cref="ToExpression"/>, compiled on first use and cached for the lifetime of
    /// this instance. The member cannot be overridden: the expression is the single definition of
    /// the predicate.
    /// </para>
    /// <para>
    /// The cache is not locked. Two threads evaluating a fresh instance concurrently may both
    /// compile the expression; each compiles the same expression into an equivalent delegate, so the
    /// race costs a compilation and never yields a wrong result. Under NativeAOT, compilation falls
    /// back to the expression interpreter.
    /// </para>
    /// </remarks>
    public bool IsSatisfiedBy(T candidate)
    {
        var predicate = _predicate ??= ToExpression().Compile();
        return predicate(candidate);
    }

    /// <summary>
    /// Converts this specification to a LINQ expression that can be used with query providers.
    /// </summary>
    /// <returns>An expression representing this specification's logic</returns>
    /// <remarks>
    /// This is the only member a derived specification implements. Return an expression the
    /// intended query provider can translate; <see cref="IsSatisfiedBy"/> compiles the same
    /// expression for in-memory evaluation.
    /// </remarks>
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