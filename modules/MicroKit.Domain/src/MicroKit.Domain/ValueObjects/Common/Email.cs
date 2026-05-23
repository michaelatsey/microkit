using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents an email address with lightweight format validation and normalization.
/// Provides case-insensitive equality and normalized storage.
/// </summary>
/// <param name="Value">The email address value</param>
public sealed record Email(string Value) : IValueObject
{
    /// <summary>
    /// Gets the normalized email address in lowercase.
    /// </summary>
    public string Value { get; } = ValidateAndNormalize(Value);

    /// <summary>
    /// Validates and normalizes an email address.
    /// </summary>
    /// <param name="email">The email address to validate</param>
    /// <returns>The normalized email address</returns>
    /// <exception cref="DomainException">Thrown when the email format is invalid</exception>
    private static string ValidateAndNormalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email address cannot be null or empty.");

        var trimmed = email.Trim();

        if (trimmed.Length < 3) // Minimum: a@b
            throw new DomainException("Email address is too short.");

        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0) // Must have @ and at least one character before it
            throw new DomainException("Email address must contain a valid @ symbol.");

        if (atIndex == trimmed.Length - 1) // Must have at least one character after @
            throw new DomainException("Email address must contain a domain after @.");

        if (trimmed.Count(c => c == '@') != 1) // Exactly one @
            throw new DomainException("Email address must contain exactly one @ symbol.");

        var localPart = trimmed.Substring(0, atIndex);
        var domainPart = trimmed.Substring(atIndex + 1);

        if (localPart.Length == 0 || localPart.Length > 64)
            throw new DomainException("Email local part length is invalid.");

        if (domainPart.Length == 0 || domainPart.Length > 253)
            throw new DomainException("Email domain part length is invalid.");

        if (trimmed.Length > 254) // RFC 5321 limit - check after parts validation
            throw new DomainException("Email address is too long.");

        if (!domainPart.Contains('.'))
            throw new DomainException("Email domain must contain at least one dot.");

        // Basic character validation (simplified)
        if (localPart.StartsWith('.') || localPart.EndsWith('.') || localPart.Contains(".."))
            throw new DomainException("Email local part contains invalid dot placement.");

        if (domainPart.StartsWith('.') || domainPart.EndsWith('.') || domainPart.Contains(".."))
            throw new DomainException("Email domain contains invalid dot placement.");

        // Normalize to lowercase for consistent storage and comparison
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Implicitly converts a string to an Email.
    /// </summary>
    /// <param name="email">The email string</param>
    /// <returns>An Email instance</returns>
    public static implicit operator Email(string email) => new(email);

    /// <summary>
    /// Implicitly converts an Email to a string.
    /// </summary>
    /// <param name="email">The Email instance</param>
    /// <returns>The email string value</returns>
    public static implicit operator string(Email email) => email.Value;

    /// <summary>
    /// Returns the email address value.
    /// </summary>
    /// <returns>The email address string</returns>
    public override string ToString() => Value;
}