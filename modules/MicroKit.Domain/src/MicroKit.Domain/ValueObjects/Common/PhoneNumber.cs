using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a phone number with normalization and international compatibility.
/// Provides basic validation while preserving international format flexibility.
/// </summary>
/// <param name="Value">The phone number value</param>
public sealed record PhoneNumber(string Value) : IValueObject
{
    /// <summary>
    /// Gets the normalized phone number value.
    /// </summary>
    public string Value { get; } = ValidateAndNormalize(Value);

    /// <summary>
    /// Validates and normalizes a phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number to validate</param>
    /// <returns>The normalized phone number</returns>
    /// <exception cref="DomainException">Thrown when the phone number format is invalid</exception>
    private static string ValidateAndNormalize(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new DomainException("Phone number cannot be null or empty.");

        var trimmed = phoneNumber.Trim();

        // Remove common formatting characters but preserve the + for international numbers
        var cleaned = new string(trimmed.Where(c => char.IsDigit(c) || c == '+' || c == 'x' || c == 'X').ToArray());

        if (string.IsNullOrEmpty(cleaned))
            throw new DomainException("Phone number must contain at least one digit.");

        // Basic length validation (international numbers can be 7-15 digits according to ITU-T E.164)
        var digitCount = cleaned.Count(char.IsDigit);
        if (digitCount < 7)
            throw new DomainException("Phone number must contain at least 7 digits.");

        if (digitCount > 15)
            throw new DomainException("Phone number cannot contain more than 15 digits.");

        // International number validation
        if (cleaned.StartsWith('+'))
        {
            if (cleaned.Count(c => c == '+') > 1)
                throw new DomainException("Phone number can contain only one '+' symbol at the beginning.");

            if (cleaned.Length == 1) // Just a '+' with no digits
                throw new DomainException("International phone number must contain digits after '+'.");
        }

        // Extension validation (if present)
        if (cleaned.Contains('x') || cleaned.Contains('X'))
        {
            var extensionIndex = Math.Max(cleaned.LastIndexOf('x'), cleaned.LastIndexOf('X'));
            var mainPart = cleaned.Substring(0, extensionIndex);
            var extensionPart = cleaned.Substring(extensionIndex + 1);

            if (string.IsNullOrEmpty(extensionPart) || !extensionPart.All(char.IsDigit))
                throw new DomainException("Phone number extension must contain only digits.");

            if (mainPart.Count(char.IsDigit) < 7)
                throw new DomainException("Phone number main part must contain at least 7 digits.");
        }

        return cleaned;
    }

    /// <summary>
    /// Implicitly converts a string to a PhoneNumber.
    /// </summary>
    /// <param name="phoneNumber">The phone number string</param>
    /// <returns>A PhoneNumber instance</returns>
    public static implicit operator PhoneNumber(string phoneNumber) => new(phoneNumber);

    /// <summary>
    /// Implicitly converts a PhoneNumber to a string.
    /// </summary>
    /// <param name="phoneNumber">The PhoneNumber instance</param>
    /// <returns>The phone number string value</returns>
    public static implicit operator string(PhoneNumber phoneNumber) => phoneNumber.Value;

    /// <summary>
    /// Returns the phone number value.
    /// </summary>
    /// <returns>The phone number string</returns>
    public override string ToString() => Value;
}