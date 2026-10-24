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
    /// Gets the stable code of the business rule that was violated.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessRuleViolationException"/> class.
    /// </summary>
    /// <param name="rule">The business rule that was violated</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the rule's <see cref="IBusinessRule.Code"/> is null, empty or white space: the code
    /// is the rule's stable contract, and a violation without one cannot be identified.
    /// </exception>
    public BusinessRuleViolationException(IBusinessRule rule)
        : base(FormatMessage(rule))
    {
        ViolatedRule = rule;
        Code = rule.Code;
    }

    private static string FormatMessage(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Code);
        return $"Business rule '{rule.Code}' was violated: {rule.Message}";
    }
}