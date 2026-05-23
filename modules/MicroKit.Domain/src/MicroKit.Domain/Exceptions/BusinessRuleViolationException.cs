using MicroKit.Domain.Rules;

namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Thrown when a business rule invariant is violated.
/// Contains reference to the specific rule that was broken.
/// </summary>
public sealed class BusinessRuleViolationException : DomainException
{
    public IBusinessRule ViolatedRule { get; }

    public BusinessRuleViolationException(IBusinessRule rule)
        : base($"Business rule '{rule.GetType().Name}' was violated: {rule.Message}")
    {
        ViolatedRule = rule;
    }
}