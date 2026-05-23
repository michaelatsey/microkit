using MicroKit.Domain.Rules;

namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Thrown when a business rule invariant is violated.
/// Contains reference to the specific rule that was broken.
/// </summary>
public sealed class BusinessRuleViolationException : DomainException
{
    /// <summary>
    /// Gets the business rule that was violated.
    /// </summary>
    public IBusinessRule ViolatedRule { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessRuleViolationException"/> class.
    /// </summary>
    /// <param name="rule">The business rule that was violated</param>
    public BusinessRuleViolationException(IBusinessRule rule)
        : base($"Business rule '{rule.GetType().Name}' was violated: {rule.Message}")
    {
        ViolatedRule = rule;
    }
}